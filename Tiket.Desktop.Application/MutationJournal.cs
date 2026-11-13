using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

// Internal until the full mutation coordinator and native review gate are validated.
internal enum MutationState { Prepared, Sending, Unknown, Acked }

internal sealed record MutationBinding(string Profile, string Backend, string Issuer, string Client,
    string Subject, string Event, int Pos)
{
    public override string ToString() => "Mutation binding (details redacted)";
}

internal sealed record MutationRecord(int Schema, Guid Id, MutationBinding Binding, string Method,
    byte[] Request, string Coupon, string Key, string Player, string Cashier, byte[] ReviewedQuote,
    DateTimeOffset CreatedAt, MutationState State, byte[]? Response, Guid? OriginalPurchase)
{
    public override string ToString() => "Encrypted mutation record (details redacted)";
}

internal interface IJournalProtection
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>One encrypted snapshot, one exclusive OS file handle, no plaintext WAL or temporary file.</summary>
internal sealed class MutationJournal : IDisposable
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream writerLock;
    private readonly IJournalProtection protection;
    private readonly Action<string, string> replace;
    private List<MutationRecord> records = [];
    private bool poisoned;
    private bool disposed;

    internal MutationJournal(string directory, IJournalProtection? protection = null,
        Action<string, string>? replace = null)
    {
        this.protection = protection ?? new CurrentUserJournalProtection();
        this.replace = replace ?? DurableReplace;
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Journal directory must not be redirected.");
        path = Path.Combine(directory, "mutations.v1.dpapi");
        var lockPath = Path.Combine(directory, "writer.lock");
        var existing = File.Exists(lockPath);
        writerLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            // A failed commit is evidence to investigate, never an invitation to initialize an empty journal.
            if (File.Exists(path + ".pending")) throw new IOException("Journal commit requires investigation.");
            if (!File.Exists(path))
            {
                if (existing) throw new IOException("Existing journal data is missing. Restore before continuing.");
                Commit([]);
            }
            else
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > MaximumBytes) throw new IOException("Journal size invalid.");
                var plaintext = this.protection.Unprotect(File.ReadAllBytes(path));
                try
                {
                    var snapshot = JsonSerializer.Deserialize<Snapshot>(plaintext)
                        ?? throw new IOException("Journal invalid.");
                    if (snapshot.Schema != 1 || snapshot.Records is null || snapshot.Records.Count > 10000)
                        throw new IOException("Unsupported journal schema.");
                    records = snapshot.Records;
                    if (records.Select(r => r.Id).Distinct().Count() != records.Count)
                        throw new IOException("Duplicate journal identity.");
                    foreach (var record in records) Validate(record);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (records.Any(r => r.State == MutationState.Sending))
                    Commit(records.Select(r => r.State == MutationState.Sending
                        ? r with { State = MutationState.Unknown } : r).ToList());
            }
        }
        catch { writerLock.Dispose(); throw; }
    }

    internal MutationRecord Prepare(MutationRecord record)
    {
        lock (gate)
        {
            Ready();
            Validate(record);
            if (record.State != MutationState.Prepared || record.Response is not null || records.Count >= 10000)
                throw new InvalidOperationException("Invalid new intent or journal quota reached.");
            if (record.OriginalPurchase is { } parent && !records.Any(r => r.Id == parent &&
                    r.State == MutationState.Acked && r.OriginalPurchase is null && r.Binding == record.Binding &&
                    r.Coupon == record.Coupon && r.Player == record.Player && r.Cashier == record.Cashier))
                throw new InvalidOperationException("Reprint requires an acknowledged original in the same context.");
            if (records.Any(r => r.Id == record.Id || (r.Binding == record.Binding &&
                (r.Key == record.Key || (r.Method == record.Method && r.Coupon == record.Coupon && record.OriginalPurchase is null)))))
                throw new InvalidOperationException("Intent already exists; recover its original identity.");
            var copy = Clone(record);
            Commit([.. records, copy]);
            return Clone(copy);
        }
    }

    internal IReadOnlyList<MutationRecord> Read(MutationBinding binding)
    {
        lock (gate) { Ready(); return records.Where(r => r.Binding == binding).Select(Clone).ToArray(); }
    }

    internal MutationRecord MarkSending(Guid id, MutationBinding binding) => Transition(id, binding, MutationState.Sending, null);
    internal MutationRecord MarkUnknown(Guid id, MutationBinding binding) => Transition(id, binding, MutationState.Unknown, null);
    internal MutationRecord Acknowledge(Guid id, MutationBinding binding, byte[] response) =>
        Transition(id, binding, MutationState.Acked, response.Length > 0 ? response : throw new ArgumentException("Empty acknowledgement."));

    private MutationRecord Transition(Guid id, MutationBinding binding, MutationState next, byte[]? response)
    {
        lock (gate)
        {
            Ready();
            var old = records.SingleOrDefault(r => r.Id == id && r.Binding == binding)
                ?? throw new InvalidOperationException("Intent unavailable in this authenticated context.");
            var allowed = next switch
            {
                MutationState.Sending => old.State is MutationState.Prepared or MutationState.Unknown,
                MutationState.Unknown => old.State == MutationState.Sending,
                MutationState.Acked => old.State == MutationState.Sending,
                _ => false
            };
            if (!allowed) throw new InvalidOperationException("Invalid journal transition.");
            var value = old with { State = next, Response = response?.ToArray() };
            Commit(records.Select(r => r.Id == id ? value : r).ToList());
            return Clone(value);
        }
    }

    private void Commit(List<MutationRecord> next)
    {
        byte[]? plaintext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(new Snapshot(1, next));
            if (plaintext.Length > MaximumBytes / 2) throw new IOException("Journal quota reached.");
            var encrypted = protection.Protect(plaintext);
            if (encrypted.Length is <= 0 or > MaximumBytes) throw new IOException("Journal encryption failed.");
            using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(encrypted);
                file.Flush(true);
            }
            replace(path + ".pending", path);
            // Verify the committed encrypted snapshot before allowing dispatch.
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(encrypted)) throw new IOException("Journal verification failed.");
            records = next;
        }
        catch { poisoned = true; throw; }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static void Validate(MutationRecord r)
    {
        if (r.Schema != 1 || r.Id == Guid.Empty || r.Binding is null ||
            new[] { r.Binding.Profile, r.Binding.Backend, r.Binding.Issuer, r.Binding.Client,
                r.Binding.Subject, r.Binding.Event, r.Coupon, r.Key, r.Player, r.Cashier }.Any(string.IsNullOrWhiteSpace) ||
            r.Binding.Pos <= 0 || r.Request is not { Length: > 0 } || r.ReviewedQuote is not { Length: > 0 } ||
            !Enum.IsDefined(r.State) || (r.State == MutationState.Acked) != (r.Response is { Length: > 0 }) ||
            r.Method is not ("pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon" or
                "pl.umamusume.tiket.api.v1.PrintingService/ReprintCoupon"))
            throw new IOException("Journal record invalid or unsupported.");
        if (r.Method.EndsWith("/PurchaseCoupon", StringComparison.Ordinal))
        {
            var request = PurchaseCouponRequest.Parser.ParseFrom(r.Request);
            if (r.OriginalPurchase is not null || request.CouponId != r.Coupon || request.IdempotencyKey != r.Key ||
                request.PlayerId != r.Player || request.RaceDayId != r.Binding.Event ||
                request.PointOfSaleNumber != r.Binding.Pos || request.CashierIdentifier != r.Cashier)
                throw new IOException("Journal purchase binding invalid.");
        }
        else
        {
            var request = ReprintCouponRequest.Parser.ParseFrom(r.Request);
            if (r.OriginalPurchase is null || request.CouponId != r.Coupon || request.IdempotencyKey != r.Key ||
                request.RaceDayId != r.Binding.Event) throw new IOException("Journal reprint binding invalid.");
        }
    }

    private void Ready()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (poisoned) throw new IOException("Journal quarantined after persistence failure.");
    }
    private static MutationRecord Clone(MutationRecord r) => r with
    { Request = r.Request.ToArray(), ReviewedQuote = r.ReviewedQuote.ToArray(), Response = r.Response?.ToArray() };
    private sealed record Snapshot(int Schema, List<MutationRecord> Records);
    private static void DurableReplace(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows journal required.");
        if (!MoveFileEx(source, destination, 0x1 | 0x8)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; writerLock.Dispose(); } }
}

/// <summary>DPAPI CurrentUser. This does not isolate OIDC cashiers sharing a Windows account.</summary>
internal sealed class CurrentUserJournalProtection : IJournalProtection
{
    public byte[] Protect(byte[] plaintext) => Transform(plaintext, true);
    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, false);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows user journal protection failed.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
