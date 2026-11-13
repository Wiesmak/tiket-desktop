using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class ConnectionTests
{
    internal static readonly Guid Day = Guid.Parse("46fa1b57-bb05-42a7-a866-46dabbe47784");
    internal static ConnectionProfile Profile(string backend = "https://api.example.invalid", string issuer = "https://identity.example.invalid",
        bool development = false) => new("test", "Test", backend, issuer, "desktop", "tiket-api",
        ["openid", "tiket.read"], [Day.ToString()], development);

    [Theory]
    [InlineData("http://remote.example", false)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://127.0.0.1.evil:8080", true)]
    [InlineData("http://127.1:8080", false)]
    [InlineData("https://user:password@example.invalid", false)]
    [InlineData("https://api.example.invalid?token=secret", false)]
    [InlineData("https://api.example.invalid/path", false)]
    [InlineData("https://api.example.invalid./", false)]
    public void RejectConfusingBackend(string backend, bool development) =>
        Assert.Throws<ArgumentException>(() => Profile(backend, development: development));

    [Fact] public void ValidProfilesAreImmutable()
    {
        Assert.Equal("https", Profile().Backend.Scheme);
        Assert.Equal("http", Profile("http://127.0.0.1:8888", development: true).Backend.Scheme);
        Assert.Throws<ArgumentException>(() => Profile(issuer: "http://127.0.0.1:8888"));
        Assert.Throws<ArgumentException>(() => new ConnectionProfile("../bad", "x", "https://api.example", "https://issuer.example", "c", "a", ["openid"], []));
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated, ConnectionState.Expired)]
    [InlineData(StatusCode.PermissionDenied, ConnectionState.Denied)]
    [InlineData(StatusCode.NotFound, ConnectionState.UnknownEvent)]
    [InlineData(StatusCode.Unavailable, ConnectionState.Offline)]
    [InlineData(StatusCode.DeadlineExceeded, ConnectionState.Offline)]
    [InlineData(StatusCode.Cancelled, ConnectionState.Cancelled)]
    public void StatusMapping(StatusCode code, ConnectionState expected) => Assert.Equal(expected, ConnectionErrors.State(code));

    [Fact] public async Task SignOutSuppressesLateReadAndClearsIdentity()
    {
        var server = new LateBackend();
        using var session = new ConnectionSession(new TestLogin(), _ => server);
        session.Select(Profile());
        await session.SignInAsync();
        var read = session.ReadAsync(Day);
        session.SignOut();
        server.Completion.SetResult(new EventOverview(new RaceDayInformation(), new RaceList(), DateTimeOffset.UtcNow));
        await read;
        Assert.True(server.Cancelled.IsCancellationRequested);
        Assert.True(server.Disposed);
        Assert.Null(session.Subject);
        Assert.Null(session.ExpiresAt);
        Assert.Null(session.Overview);
        Assert.Equal(ConnectionState.SignedOut, session.State);
    }

    [Fact] public async Task ProfileSwitchSuppressesOldLogin()
    {
        var login = new LateLogin();
        using var session = new ConnectionSession(login);
        session.Select(Profile());
        var pending = session.SignInAsync();
        session.Select(Profile("https://other.example.invalid"));
        login.Completion.SetResult(new LoginMaterial("old-subject", "access-only", DateTimeOffset.UtcNow.AddMinutes(5)));
        await pending;
        Assert.Null(session.Subject);
        Assert.Equal(ConnectionState.SignedOut, session.State);
        Assert.Null(new ConnectionSession().Subject);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, true)]
    [InlineData(StatusCode.Unavailable, true)]
    [InlineData(StatusCode.Unauthenticated, false)]
    public async Task DenialAndOutageRetainSessionButRejectedCredentialsClearIt(StatusCode code, bool retain)
    {
        var server = new LateBackend();
        using var session = new ConnectionSession(new TestLogin(), _ => server);
        session.Select(Profile());
        await session.SignInAsync();
        var read = session.ReadAsync(Day);
        server.Completion.SetException(new RpcException(new Status(code, "Do not expose raw server text")));
        await read;
        Assert.Equal(retain, session.Subject is not null);
        Assert.Null(session.Overview);
        Assert.Equal(ConnectionErrors.State(code), session.State);
    }

    [Fact] public async Task ExpiryClearsCredentialsBeforeRead()
    {
        var login = new LateLogin();
        using var session = new ConnectionSession(login);
        session.Select(Profile());
        var pending = session.SignInAsync();
        login.Completion.SetResult(new LoginMaterial("subject", "access-only", DateTimeOffset.UtcNow.AddMilliseconds(100)));
        await pending;
        await Task.Delay(150);
        session.CheckExpiry();
        Assert.Null(session.Subject);
        Assert.Null(session.ExpiresAt);
        Assert.Null(session.Overview);
        Assert.Equal(ConnectionState.Expired, session.State);
    }

    [Fact] public async Task ManagedJsonLoadsAndRejectsUnknownCredentialFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            var profile = new { Version = 1, Profiles = new[] { new {
                Id = "managed", DisplayName = "Managed", Backend = "https://api.example.invalid",
                Issuer = "https://identity.example.invalid", ClientId = "desktop", ApiAudience = "tiket-api",
                Scopes = new[] { "openid" }, KnownEvents = new[] { Day.ToString() }, DevelopmentLoopback = false
            } } };
            var json = System.Text.Json.JsonSerializer.Serialize(profile);
            await File.WriteAllTextAsync(path, json);
            Assert.Single(ManagedProfiles.Load(path));
            await File.WriteAllTextAsync(path, json.Replace("\"ClientId\":", "\"AccessToken\":\"forbidden\",\"ClientId\":"));
            Assert.Throws<System.Text.Json.JsonException>(() => ManagedProfiles.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact] public async Task ExplicitReadCannotReturnIntoAnotherEventContext()
    {
        var server = new LateBackend();
        using var session = new ConnectionSession(new TestLogin(), _ => server);
        session.Select(Profile()); await session.SignInAsync();
        var pending = session.ReadAsync(Day);
        session.SelectEvent(Guid.NewGuid());
        server.Completion.SetResult(new EventOverview(new RaceDayInformation(), new RaceList(), DateTimeOffset.UtcNow));
        await pending;
        Assert.True(server.Cancelled.IsCancellationRequested);
        Assert.Null(session.Overview);
        Assert.Equal(ConnectionState.Connecting, session.State);
    }
    private sealed class TestLogin : INativeLogin
    {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) =>
            Task.FromResult(new LoginMaterial("subject", "access-only", DateTimeOffset.UtcNow.AddMinutes(1)));
    }
    private sealed class LateLogin : INativeLogin
    {
        public TaskCompletionSource<LoginMaterial> Completion = new();
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) => Completion.Task;
    }
    private sealed class LateBackend : IReadOnlyBackend
    {
        public TaskCompletionSource<EventOverview> Completion = new();
        public CancellationToken Cancelled;
        public bool Disposed;
        public Task<PingResponse> PingAsync(CancellationToken ct) => Task.FromResult(new PingResponse());
        public Task<EventOverview> ReadAsync(Guid day, string accessToken, CancellationToken ct) { Cancelled = ct; return Completion.Task; }
        public void Dispose() => Disposed = true;
    }
}
