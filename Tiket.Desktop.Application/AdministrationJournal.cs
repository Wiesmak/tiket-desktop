using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

internal sealed record AdministrationBinding(string Profile, string Backend, string Issuer, string Client, string Subject, string Event)
{
    public override string ToString() => "Administration context";
}
internal sealed record AdministrationRecord(int Schema, Guid Id, AdministrationBinding Binding, string Method,
    byte[] Request, string Key, DateTimeOffset CreatedAt, MutationState State, byte[]? Response)
{
    public override string ToString() => "Encrypted administration command";
}

/// <summary>One encrypted snapshot, one exclusive OS file handle, no plaintext WAL or temporary file.</summary>
internal sealed class AdministrationJournal : IDisposable
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream writerLock;
    private readonly IJournalProtection protection;
    private readonly Action<string, string> replace;
    private List<AdministrationRecord> records = [];
    private bool poisoned;
    private bool disposed;

    internal AdministrationJournal(string directory, IJournalProtection? protection = null,
        Action<string, string>? replace = null)
    {
        this.protection = protection ?? new CurrentUserJournalProtection();
        this.replace = replace ?? DurableReplace;
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Journal directory must not be redirected.");
        path = Path.Combine(directory, "administration.v1.dpapi");
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

    internal AdministrationRecord Prepare(AdministrationRecord record)
    {
        lock (gate)
        {
            Ready();
            Validate(record);
            if (record.State != MutationState.Prepared || record.Response is not null || records.Count >= 10000)
                throw new InvalidOperationException("Invalid new intent or journal quota reached.");
            if (records.Any(r => r.Id == record.Id || (r.Binding == record.Binding && r.Key == record.Key)))
                throw new InvalidOperationException("Intent already exists; recover its original identity.");
            var copy = Clone(record);
            Commit([.. records, copy]);
            return Clone(copy);
        }
    }

    internal IReadOnlyList<AdministrationRecord> Read(AdministrationBinding binding)
    {
        lock (gate) { Ready(); return records.Where(r => r.Binding == binding).Select(Clone).ToArray(); }
    }

    internal AdministrationRecord MarkSending(Guid id, AdministrationBinding binding) => Transition(id, binding, MutationState.Sending, null);
    internal AdministrationRecord MarkUnknown(Guid id, AdministrationBinding binding) => Transition(id, binding, MutationState.Unknown, null);
    internal AdministrationRecord Acknowledge(Guid id, AdministrationBinding binding, byte[] response) =>
        Transition(id, binding, MutationState.Acked, response.Length > 0 ? response : throw new ArgumentException("Empty acknowledgement."));

    private AdministrationRecord Transition(Guid id, AdministrationBinding binding, MutationState next, byte[]? response)
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

    private void Commit(List<AdministrationRecord> next)
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

    private static void Validate(AdministrationRecord r)
    {
        if (r.Schema != 1 || r.Id == Guid.Empty || r.Binding is null ||
            new[] { r.Binding.Profile, r.Binding.Backend, r.Binding.Issuer, r.Binding.Client,
                r.Binding.Subject, r.Binding.Event, r.Key }.Any(string.IsNullOrWhiteSpace) ||
            r.Request is not { Length: > 0 } || !Enum.IsDefined(r.State) ||
            (r.State == MutationState.Acked) != (r.Response is { Length: > 0 }))
            throw new IOException("Invalid administration record.");
        var (day, key) = AdministrationWire.Identity(r.Method, r.Request);
        if (day != r.Binding.Event || key != r.Key)
            throw new IOException("Administration request context mismatch.");
    }

    private void Ready()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (poisoned) throw new IOException("Journal quarantined after persistence failure.");
    }
    private static AdministrationRecord Clone(AdministrationRecord r) => r with
    { Request = r.Request.ToArray(), Response = r.Response?.ToArray() };
    private sealed record Snapshot(int Schema, List<AdministrationRecord> Records);
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

