using System.Security.AccessControl;
using System.Security.Principal;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tiket.Desktop.Application;

internal static class ProtectedLocalStorage
{
    internal static string DirectoryFor(string purpose)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tiket Desktop", purpose, "v1");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) throw new IOException("Local fixed storage required.");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows identity unavailable.");
        var info = new DirectoryInfo(path);
        if (!info.Exists)
        {
            info.Create();
            var acl = new DirectorySecurity(); acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected storage rejected.");
        var current = info.GetAccessControl();
        if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("Storage owner mismatch.");
        foreach (FileSystemAccessRule rule in current.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(owner) &&
                rule.IdentityReference.Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new IOException("Storage ACL requires review.");
        return path;
    }
    internal static void Replace(string source, string target)
    {
        if (!MoveFileEx(source, target, 9)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileEx(string source, string target, uint flags);
}

internal sealed class LocalPlanStore(string directory)
{
    private sealed record Saved(int Schema, AdministrationBinding Binding, byte[] Plan);
    private static string Name(AdministrationBinding binding) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(binding)));
    private string PathFor(AdministrationBinding binding) => Path.Combine(directory, Name(binding) + ".plan.dpapi");
    internal void Save(AdministrationBinding binding, LocalRaceDayPlan plan)
    {
        Directory.CreateDirectory(directory);
        using var writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = PathFor(binding);
        if (File.Exists(path + ".pending")) throw new IOException("Draft commit requires investigation.");
        if (File.Exists(path))
        {
            var previous = Read(path, binding);
            if (previous.IsFrozen && !previous.Serialize().AsSpan().SequenceEqual(plan.Serialize()))
                throw new IOException("Frozen plan cannot be replaced.");
        }
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Saved(1, binding, plan.Serialize()));
        try
        {
            if (plain.Length > 4 * 1024 * 1024) throw new IOException("Draft too large.");
            var encrypted = new CurrentUserJournalProtection().Protect(plain);
            using (var file = new FileStream(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(encrypted); file.Flush(true); }
            ProtectedLocalStorage.Replace(path + ".pending", path);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(encrypted)) throw new IOException("Draft commit verification failed.");
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static LocalRaceDayPlan Read(string path, AdministrationBinding binding)
    {
        if (new FileInfo(path).Length is <= 0 or > 8 * 1024 * 1024) throw new IOException("Invalid draft size.");
        var plain = new CurrentUserJournalProtection().Unprotect(File.ReadAllBytes(path));
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(plain) ?? throw new IOException("Invalid draft.");
            if (saved.Schema != 1 || saved.Binding != binding) throw new IOException("Draft context mismatch.");
            var plan = LocalRaceDayPlan.Deserialize(saved.Plan);
            if (plan.Definition.RaceDayId != binding.Event) throw new IOException("Draft event mismatch.");
            return plan;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal LocalRaceDayPlan? Load(AdministrationBinding binding)
    {
        Directory.CreateDirectory(directory);
        using var writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = PathFor(binding);
        if (File.Exists(path + ".pending")) throw new IOException("Draft commit requires investigation.");
        return File.Exists(path) ? Read(path, binding) : null;
    }
    internal void Discard(AdministrationBinding binding)
    {
        Directory.CreateDirectory(directory);
        using var writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = PathFor(binding);
        if (File.Exists(path + ".pending")) throw new IOException("Draft commit requires investigation.");
        if (File.Exists(path))
        {
            if (Read(path, binding).IsFrozen) throw new IOException("Frozen plan must be created or recovered.");
            File.Delete(path);
        }
    }
}
