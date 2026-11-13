using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class OidcFlowTests
{
    [Theory]
    [InlineData("access-valid", true)]
    [InlineData("access-subject", false)]
    [InlineData("access-audience", false)]
    [InlineData("access-issuer", false)]
    [InlineData("access-role", false)]
    [InlineData("access-pos", false)]
    [InlineData("access-pos-leadingzero", false)]
    [InlineData("access-pos-sign", false)]
    [InlineData("access-signature", false)]
    public async Task FinancialContextRequiresVerifiedAccessTokenBindings(string scenario, bool permitted)
    {
        using var issuer = new SyntheticIssuer(scenario);
        var login = new NativeOidcLogin(issuer.BrowserAsync, () => issuer);
        var material = await login.SignInAsync(ConnectionTests.Profile(), _ => { }, CancellationToken.None);
        Assert.Equal(permitted, material.OperatingGrants is not null);
        if (permitted)
        {
            Assert.Equal("fixture-cashier", material.OperatingGrants!.Cashier);
            Assert.Equal(new[] { 7 }, material.OperatingGrants.Poses);
            Assert.NotEqual(material.Subject, material.OperatingGrants.Cashier);
        }
    }
    [Theory]
    [InlineData("access-eventadmin", true)] [InlineData("access-role", true)]
    [InlineData("access-subject", false)] [InlineData("access-audience", false)]
    [InlineData("access-issuer", false)] [InlineData("access-signature", false)]
    public async Task AdministrativeRolesRequireSignedSameSubjectApiToken(string scenario, bool allowed)
    {
        using var issuer = new SyntheticIssuer(scenario);
        var material = await new NativeOidcLogin(issuer.BrowserAsync, () => issuer)
            .SignInAsync(ConnectionTests.Profile(), _ => {}, default);
        Assert.Equal(allowed, material.AdministrationGrants is not null);
        if (allowed)
        {
            Assert.Null(material.OperatingGrants);
            Assert.Contains(scenario == "access-eventadmin" ? "EVENT_ADMIN" : "AUDITOR", material.AdministrationGrants!.Roles);
            Assert.Contains(ConnectionTests.Day.ToString(), material.AdministrationGrants.Events);
        }
    }
    [Theory]
    [InlineData("valid")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("nonce")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("bad-code")]
    [InlineData("discovery-issuer")]
    [InlineData("unsupported-pkce")]
    [InlineData("par-rejected")]
    public async Task CompleteCodePkceWithSignedDiscoveryFixture(string scenario)
    {
        using var issuer = new SyntheticIssuer(scenario);
        var login = new NativeOidcLogin(issuer.BrowserAsync, () => issuer);
        if (scenario == "valid")
        {
            var result = await login.SignInAsync(ConnectionTests.Profile(), _ => { }, CancellationToken.None);
            Assert.Equal("fixture-subject", result.Subject);
            Assert.Equal("api-access-only", result.AccessToken);
            Assert.True(issuer.PkceVerified);
            Assert.DoesNotContain("api-access-only", result.ToString());
        }
        else if (scenario == "par-rejected")
        {
            var error = await Assert.ThrowsAsync<SignInFailure>(() => login.SignInAsync(ConnectionTests.Profile(), _ => { }, CancellationToken.None));
            Assert.Equal(ConnectionState.DiscoveryError, error.State);
            Assert.False(issuer.BrowserOpened);
        }
        else await Assert.ThrowsAnyAsync<InvalidOperationException>(() => login.SignInAsync(ConnectionTests.Profile(), _ => { }, CancellationToken.None));
    }

    [Fact] public async Task CallbackRejectsWrongPathStateAndReplay()
    {
        using var callback = new LoopbackCallback();
        using var other = new LoopbackCallback();
        Assert.Equal("127.0.0.1", callback.RedirectUri.Host);
        Assert.NotEqual(callback.RedirectUri.Port, other.RedirectUri.Port);
        var pending = callback.ReceiveAsync("expected", TimeSpan.FromSeconds(5), CancellationToken.None);
        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(new Uri(callback.RedirectUri, "/wrong?state=expected&code=x"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(callback.RedirectUri + "?state=wrong&code=x")).StatusCode);
        Assert.False(pending.IsCompleted);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(callback.RedirectUri + "?state=expected&code=x")).StatusCode);
        Assert.Contains("code=x", await pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => callback.ReceiveAsync("expected", TimeSpan.FromSeconds(1), CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(callback.RedirectUri));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallbackCancellationAndTimeoutCloseSocket(bool cancel)
    {
        using var callback = new LoopbackCallback();
        using var source = new CancellationTokenSource();
        var pending = callback.ReceiveAsync("expected", cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(30), source.Token);
        if (cancel) source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        using var http = new HttpClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(callback.RedirectUri));
    }

    // In-process protocol transport is injectable only through an internal test constructor.
    // Discovery stays HTTPS; the real callback is TCP loopback. No production TLS bypass exists.
    private sealed class SyntheticIssuer(string scenario) : HttpMessageHandler
    {
        private readonly RSA rsa = RSA.Create(2048);
        private Dictionary<string, string> authorization = new();
        public bool PkceVerified;
        public bool BrowserOpened;
        public async Task BrowserAsync(Uri uri)
        {
            BrowserOpened = true;
            authorization = LoopbackCallback.ParseQuery(uri.Query);
            Assert.Equal("code", authorization["response_type"]);
            Assert.Equal("S256", authorization["code_challenge_method"]);
            Assert.False(authorization.ContainsKey("client_secret"));
            using var http = new HttpClient();
            await http.GetAsync(authorization["redirect_uri"] + "?state=" + authorization["state"] + "&code=" +
                (scenario == "bad-code" ? "substituted" : "one-use-code"));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object payload;
            if (request.RequestUri!.AbsolutePath.EndsWith("openid-configuration"))
                payload = new
                {
                    issuer = scenario == "discovery-issuer" ? "https://wrong.example.invalid" : "https://identity.example.invalid",
                    authorization_endpoint = "https://identity.example.invalid/authorize",
                    token_endpoint = "https://identity.example.invalid/token",
                    pushed_authorization_request_endpoint = scenario == "par-rejected" ? "https://identity.example.invalid/par" : null,
                    jwks_uri = "https://identity.example.invalid/jwks",
                    response_types_supported = new[] { "code" }, code_challenge_methods_supported = new[] { scenario == "unsupported-pkce" ? "plain" : "S256" },
                    subject_types_supported = new[] { "public" }, id_token_signing_alg_values_supported = new[] { "RS256" },
                    token_endpoint_auth_methods_supported = new[] { "none" }
                };
            else if (request.RequestUri.AbsolutePath == "/par")
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"error\":\"invalid_request\",\"error_description\":\"Invalid scopes\"}", Encoding.UTF8, "application/json") };
            else if (request.RequestUri.AbsolutePath == "/jwks")
            {
                var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa) { KeyId = "fixture" });
                payload = new { keys = new[] { new { kty = key.Kty, n = key.N, e = key.E, kid = "fixture", alg = "RS256", use = "sig" } } };
            }
            else
            {
                var form = LoopbackCallback.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.False(form.ContainsKey("client_secret"));
                var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"])));
                PkceVerified = challenge == authorization["code_challenge"];
                Assert.True(PkceVerified);
                Assert.Equal(authorization["redirect_uri"], form["redirect_uri"]);
                if (form["code"] != "one-use-code")
                    return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json") };
                using var wrongKey = RSA.Create(2048);
                var signing = new RsaSecurityKey(scenario == "signature" ? wrongKey : rsa) { KeyId = "fixture" };
                var now = DateTime.UtcNow;
                var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = scenario == "issuer" ? "https://evil.example.invalid" : "https://identity.example.invalid",
                    Audience = scenario == "audience" ? "wrong" : "desktop",
                    Claims = new Dictionary<string, object>
                    {
                        ["sub"] = "fixture-subject", ["nonce"] = scenario == "nonce" ? "wrong" : authorization["nonce"]
                    },
                    IssuedAt = now.AddMinutes(-10), NotBefore = now.AddMinutes(-10),
                    Expires = scenario == "expired" ? now.AddMinutes(-5) : now.AddMinutes(5),
                    SigningCredentials = new SigningCredentials(signing, SecurityAlgorithms.RsaSha256)
                });
                var access = "api-access-only";
                if (scenario.StartsWith("access-")) access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = scenario == "access-issuer" ? "https://other.invalid" : "https://identity.example.invalid",
                    Audience = scenario == "access-audience" ? "other" : "tiket-api",
                    Expires = DateTime.UtcNow.AddMinutes(5),
                    Claims = new Dictionary<string, object>
                    {
                        ["sub"] = scenario == "access-subject" ? "other-subject" : "fixture-subject",
                        ["tiket_roles"] = new[] { scenario == "access-role" ? "AUDITOR" : scenario == "access-eventadmin" ? "EVENT_ADMIN" : "CASHIER" },
                        ["tiket_cashier"] = "fixture-cashier", ["tiket_events"] = new[] { ConnectionTests.Day.ToString() },
                        ["tiket_players"] = new[] { Guid.NewGuid().ToString() },
                        ["tiket_pos"] = new[] { scenario switch { "access-pos" => "invalid", "access-pos-leadingzero" => "07", "access-pos-sign" => "+7", _ => "7" } }
                    },
                    SigningCredentials = new SigningCredentials(new RsaSecurityKey(scenario == "access-signature" ? wrongKey : rsa)
                        { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256)
                });
                payload = new { access_token = access, id_token = jwt, expires_in = 300, token_type = "Bearer" };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions
                { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { rsa.Dispose(); base.Dispose(disposing); }
    }
}
