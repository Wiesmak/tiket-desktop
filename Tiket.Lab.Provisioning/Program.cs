using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Disposable lab capability. This executable is never launched by a production profile.
// Administrator credentials belong to the service environment, never to desktop/source files.
const string issuer = "https://localhost:8443/realms/tiket-lab";
var adminUser = Environment.GetEnvironmentVariable("TIKET_LAB_ADMIN_USER") ?? throw new InvalidOperationException("Set lab identity administrator user in the service environment.");
var adminPassword = Environment.GetEnvironmentVariable("TIKET_LAB_ADMIN_PASSWORD") ?? throw new InvalidOperationException("Set lab identity administrator password in the service environment.");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:9091");
var app = builder.Build();
using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
var serial = new SemaphoreSlim(1);
app.MapPost("/provision", async (HttpContext context, ProvisionRequest request) => {
    if (context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote) ||
        request.Event == Guid.Empty || request.Players is null || request.Players.Length > 1000 || request.Players.Any(p => p == Guid.Empty))
        return Results.BadRequest();
    var bearer = context.Request.Headers.Authorization.ToString();
    if (!bearer.StartsWith("Bearer ", StringComparison.Ordinal)) return Results.Unauthorized();
    try
    {
        var jwks = await http.GetStringAsync(issuer + "/protocol/openid-connect/certs", context.RequestAborted);
        var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(bearer[7..], new TokenValidationParameters {
            ValidIssuer = issuer, ValidAudience = "tiket-api", IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], RequireSignedTokens = true, RequireExpirationTime = true,
            ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
        });
        if (!validated.IsValid || validated.SecurityToken is not JsonWebToken jwt || !jwt.TryGetPayloadValue<string>("azp", out var azp) || azp != "tiket-desktop" ||
            !Guid.TryParseExact(jwt.Subject, "D", out var subject)) return Results.Unauthorized();
        if (!jwt.TryGetPayloadValue<string[]>("tiket_roles", out var roles) || !roles.Contains("EVENT_ADMIN"))
            return Results.StatusCode(403);
        await serial.WaitAsync(context.RequestAborted);
        try
        {
            using var login = await http.PostAsync("https://localhost:8443/realms/master/protocol/openid-connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "password", ["client_id"] = "admin-cli",
                    ["username"] = adminUser, ["password"] = adminPassword }), context.RequestAborted);
            login.EnsureSuccessStatusCode();
            var token = JsonNode.Parse(await login.Content.ReadAsStringAsync(context.RequestAborted))!["access_token"]!.GetValue<string>();
            var userUri = $"https://localhost:8443/admin/realms/tiket-lab/users/{subject:D}";
            using var read = new HttpRequestMessage(HttpMethod.Get, userUri);
            read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var current = await http.SendAsync(read, context.RequestAborted); current.EnsureSuccessStatusCode();
            var user = JsonNode.Parse(await current.Content.ReadAsStringAsync(context.RequestAborted))!.AsObject();
            var attributes = user["attributes"] as JsonObject ?? new JsonObject();
            if (user["attributes"] is null) user["attributes"] = attributes;
            Union(attributes, "tiket_events", [request.Event.ToString("D")]);
            Union(attributes, "tiket_players", request.Players.Select(p => p.ToString("D")));
            using var update = new HttpRequestMessage(HttpMethod.Put, userUri) { Content = new StringContent(user.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
            update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(update, context.RequestAborted); response.EnsureSuccessStatusCode();
            // A lost response is safe to retry: this only unions explicit grants for the authenticated subject.
            // Business creation/enrollment remains in existing journaled gRPC services.
            return Results.Ok(new { request.Event, Players = request.Players.Distinct().Count(), Reauthenticate = true });
        }
        finally { serial.Release(); }
    }
    catch (Exception) { return Results.Problem("Lab provisioning could not be confirmed. Retain the same event/player UUIDs and retry."); }
});
app.Run();

static void Union(JsonObject attributes, string key, IEnumerable<string> additions)
{
    var values = (attributes[key] as JsonArray)?.Select(v => v!.GetValue<string>()) ?? [];
    attributes[key] = new JsonArray(values.Concat(additions).Distinct(StringComparer.Ordinal).Select(s => JsonValue.Create(s)).ToArray());
}
public sealed record ProvisionRequest(Guid Event, Guid[] Players);
