using System.Diagnostics;
using Duende.IdentityModel.OidcClient;

namespace Tiket.Desktop.Application;

public sealed class LoginMaterial
{
    public string Subject { get; }
    internal string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }
    internal AdministrationGrants? AdministrationGrants { get; init; }
    internal OperatingGrants? OperatingGrants { get; init; }
    public LoginMaterial(string subject, string accessToken, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(accessToken) ||
            accessToken.Any(char.IsWhiteSpace) || expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Invalid authenticated session.");
        Subject = subject;
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
    }
    public override string ToString() => "Authenticated session (credentials redacted)";
}

public interface INativeLogin
{
    Task<LoginMaterial> SignInAsync(ConnectionProfile profile, Action<ConnectionState> progress, CancellationToken cancellationToken);
}

internal sealed class SignInFailure(ConnectionState state) : InvalidOperationException("Sign-in failed; diagnostic details redacted.")
{
    public ConnectionState State { get; } = state;
    public string? SafeDiagnostic { get; init; }
}

public sealed class NativeOidcLogin : INativeLogin
{
    private readonly Func<Uri, Task> openBrowser;
    private Func<HttpMessageHandler> handlerFactory = () => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
    internal NativeOidcLogin(Func<Uri, Task> openBrowser, Func<HttpMessageHandler> handlerFactory) : this(openBrowser) => this.handlerFactory = handlerFactory;
    public NativeOidcLogin(Func<Uri, Task>? openBrowser = null) => this.openBrowser = openBrowser ?? (uri =>
    {
        // Shell activation may reuse an existing browser and return no process handle.
        // Only an exception proves launch failure; a null handle is a valid shell result.
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    });

    public async Task<LoginMaterial> SignInAsync(ConnectionProfile profile, Action<ConnectionState> progress, CancellationToken cancellationToken)
    {
        using var callback = BindCallback();
        using var handler = new OidcDiscoveryGuard(handlerFactory(), profile.Issuer.OriginalString);
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var client = new OidcClient(new OidcClientOptions
        {
            Authority = profile.Issuer.OriginalString,
            ClientId = profile.ClientId,
            Scope = string.Join(" ", profile.Scopes),
            RedirectUri = callback.RedirectUri.AbsoluteUri,
            LoadProfile = false,
            IdentityTokenValidator = new SignedIdentityValidator(nonce),
            BackchannelHandler = handler,
            ClockSkew = TimeSpan.FromSeconds(30),
            Policy = new Policy { RequireIdentityTokenSignature = true, ValidateTokenIssuerName = true }
        });
        progress(ConnectionState.Discovering);
        Duende.IdentityModel.OidcClient.AuthorizeState prepared;
        try { prepared = await client.PrepareLoginAsync(new Duende.IdentityModel.Client.Parameters { { "nonce", nonce }, { "prompt", "login" } }, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new SignInFailure(ConnectionState.DiscoveryError); }
        if (prepared.IsError || !Uri.TryCreate(prepared.StartUrl, UriKind.Absolute, out var startUri) ||
            startUri.Scheme != "https")
            throw new SignInFailure(ConnectionState.DiscoveryError)
            { SafeDiagnostic = "Authorization preparation failed before browser activation." };
        if (!string.Equals(client.Options.ProviderInformation.IssuerName, profile.Issuer.OriginalString, StringComparison.Ordinal))
            throw new SignInFailure(ConnectionState.DiscoveryError);
        progress(ConnectionState.BrowserPending);
        using var pendingCallback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receive = callback.ReceiveAsync(prepared.State, TimeSpan.FromMinutes(3), pendingCallback.Token);
        string response;
        try
        {
            try { await openBrowser(startUri); }
            catch (Exception error)
            {
                throw new SignInFailure(ConnectionState.BrowserError)
                { SafeDiagnostic = $"Browser activation: {error.GetType().Name} (0x{error.HResult:X8})" };
            }
            try { response = await receive; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { throw new SignInFailure(ConnectionState.CallbackError); }
        }
        finally
        {
            pendingCallback.Cancel();
            try { await receive; } catch (OperationCanceledException) { }
        }
        progress(ConnectionState.Validating);
        LoginResult result;
        try { result = await client.ProcessResponseAsync(response, prepared, cancellationToken: cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new SignInFailure(ConnectionState.ValidationError); }
        if (result.IsError) throw new SignInFailure(ConnectionState.ValidationError);
        // Only validated subject plus access token survive this method; ID/refresh material is not retained.
        try
        {
            var subject = result.User.FindFirst("sub")?.Value ?? "";
            var grants = await OperatingGrants.ValidateAsync(result.AccessToken, subject, profile, client.Options);
            var administration = await AdministrationGrants.ValidateAsync(result.AccessToken, subject, profile, client.Options);
            var expiry = grants is not null && grants.ExpiresAt < result.AccessTokenExpiration ? grants.ExpiresAt : result.AccessTokenExpiration;
            if (administration is not null && administration.ExpiresAt < expiry) expiry = administration.ExpiresAt;
            return new LoginMaterial(subject, result.AccessToken, expiry) { OperatingGrants = grants, AdministrationGrants = administration };
        }
        catch (ArgumentException) { throw new SignInFailure(ConnectionState.ValidationError); }
    }
    private static LoopbackCallback BindCallback()
    {
        try { return new LoopbackCallback(); }
        catch (System.Net.Sockets.SocketException) { throw new SignInFailure(ConnectionState.CallbackError); }
    }
}
