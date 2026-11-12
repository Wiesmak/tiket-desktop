using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Tiket.Desktop.Application;

/// <summary>Provisioned selectors are configuration, never authorization grants.</summary>
public sealed class ConnectionProfile
{
    public string Id { get; }
    public string DisplayName { get; }
    public Uri Backend { get; }
    public Uri Issuer { get; }
    public string ClientId { get; }
    public string ApiAudience { get; }
    public ImmutableArray<string> Scopes { get; }
    public ImmutableArray<Guid> KnownEvents { get; }
    public bool DevelopmentLoopback { get; }

    public ConnectionProfile(string id, string displayName, string backend, string issuer,
        string clientId, string apiAudience, IEnumerable<string> scopes, IEnumerable<string> knownEvents,
        bool developmentLoopback = false)
    {
        if (id is null || !Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$")) throw new ArgumentException("Invalid profile ID.");
        Id = id;
        DisplayName = Required(displayName);
        Backend = Endpoint(backend, developmentLoopback, true);
        Issuer = Endpoint(issuer, false, false);
        ClientId = Required(clientId);
        ApiAudience = Required(apiAudience);
        Scopes = scopes.Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (!Scopes.Contains("openid") || Scopes.Any(s => string.IsNullOrWhiteSpace(s) || s.Any(char.IsWhiteSpace) ||
            s.Any(c => c < 0x21 || c > 0x7e || c == '"' || c == '\\')) || Scopes.Contains("offline_access"))
            throw new ArgumentException("Scopes must include openid; offline_access is unsupported in Phase 2.");
        KnownEvents = knownEvents.Select(s => Guid.TryParseExact(s, "D", out var g) && g != Guid.Empty
            ? g : throw new ArgumentException("Known events require nonempty UUIDs.")).Distinct().ToImmutableArray();
        DevelopmentLoopback = developmentLoopback;
    }

    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl) ? value : throw new ArgumentException("A required profile value is missing or invalid.");

    private static Uri Endpoint(string value, bool development, bool backend)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            value.Contains('\\') || uri.Host.EndsWith('.') || uri.HostNameType == UriHostNameType.Unknown ||
            uri.IdnHost != uri.Host || uri.Host.Any(c => c > 127) ||
            (backend && uri.AbsolutePath != "/") ||
            (uri.Scheme != "https" && !(backend && development && uri.Scheme == "http" &&
                uri.Host == "127.0.0.1" && Regex.IsMatch(value, @"^http://127\.0\.0\.1:[0-9]+/?$") && uri.Port > 1023)))
            throw new ArgumentException("Use an exact HTTPS URI without credentials, query or fragment. Development h2c requires 127.0.0.1 and a high port.");
        return uri;
    }

    public override string ToString() => DisplayName;
}

public static class ManagedProfiles
{
    public static string PathOnMachine => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Tiket Desktop", "connections.json");

    public static IReadOnlyList<ConnectionProfile> Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<ProfileDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new ArgumentException("Missing profiles document.");
        if (document.Version != 1 || document.Profiles is null) throw new ArgumentException("Unsupported profile document.");
        var result = document.Profiles.Select(p => new ConnectionProfile(p.Id, p.DisplayName, p.Backend,
            p.Issuer, p.ClientId, p.ApiAudience, p.Scopes, p.KnownEvents, p.DevelopmentLoopback)).ToArray();
        if (result.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new ArgumentException("Duplicate profile IDs.");
        return result;
    }

    private sealed record ProfileDocument(int Version, ProfileEntry[] Profiles);
    private sealed record ProfileEntry(string Id, string DisplayName, string Backend, string Issuer, string ClientId,
        string ApiAudience, string[] Scopes, string[] KnownEvents, bool DevelopmentLoopback);
}
