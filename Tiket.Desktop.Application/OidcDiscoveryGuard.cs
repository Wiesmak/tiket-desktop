using System.Text.Json;

namespace Tiket.Desktop.Application;

/// <summary>Fail closed on unsupported native registration metadata before opening a browser.</summary>
internal sealed class OidcDiscoveryGuard(HttpMessageHandler inner, string issuer) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Scheme != "https") throw new InvalidOperationException("OIDC endpoints require HTTPS.");
        var response = await base.SendAsync(request, cancellationToken);
        if (request.RequestUri.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal) &&
            response.IsSuccessStatusCode)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("issuer", out var actual) || actual.GetString() != issuer ||
                !Supports(root, "response_types_supported", "code") ||
                !Supports(root, "code_challenge_methods_supported", "S256"))
            {
                response.Dispose();
                throw new InvalidOperationException("Issuer or Code/PKCE S256 metadata is incompatible.");
            }
        }
        return response;
    }
    private static bool Supports(JsonElement root, string property, string value) =>
        root.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array &&
        list.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == value);
}
