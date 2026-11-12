using System.Diagnostics;
using Duende.IdentityModel.OidcClient;

namespace Tiket.Desktop.Application;

public sealed class LoginMaterial
{
    public string Subject { get; }
    internal string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }
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
}

public sealed class NativeOidcLogin : INativeLogin
{
    private readonly Func<Uri, Task> openBrowser;
    private Func<HttpMessageHandler> handlerFactory = () => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
    internal NativeOidcLogin(Func<Uri, Task> openBrowser, Func<HttpMessageHandler> handlerFactory) : this(openBrowser) => this.handlerFactory = handlerFactory;
    public NativeOidcLogin(Func<Uri, Task>? openBrowser = null) => this.openBrowser = openBrowser ?? (uri =>
    {
        if (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }) is null)
            throw new InvalidOperationException("System browser could not open.");
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
        try { prepared = await client.PrepareLoginAsync(new Duende.IdentityModel.Client.Parameters { { "nonce", nonce } }, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new SignInFailure(ConnectionState.DiscoveryError); }
        if (!string.Equals(client.Options.ProviderInformation.IssuerName, profile.Issuer.OriginalString, StringComparison.Ordinal))
            throw new SignInFailure(ConnectionState.DiscoveryError);
        progress(ConnectionState.BrowserPending);
        using var pendingCallback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receive = callback.ReceiveAsync(prepared.State, TimeSpan.FromMinutes(3), pendingCallback.Token);
        string response;
        try
        {
            try { await openBrowser(new Uri(prepared.StartUrl)); }
            catch (Exception) { throw new SignInFailure(ConnectionState.BrowserError); }
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
        try { return new LoginMaterial(result.User.FindFirst("sub")?.Value ?? "", result.AccessToken, result.AccessTokenExpiration); }
        catch (ArgumentException) { throw new SignInFailure(ConnectionState.ValidationError); }
    }
    private static LoopbackCallback BindCallback()
    {
        try { return new LoopbackCallback(); }
        catch (System.Net.Sockets.SocketException) { throw new SignInFailure(ConnectionState.CallbackError); }
    }
}
