using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public interface IReadOnlyBackend : IDisposable
{
    Task<PingResponse> PingAsync(CancellationToken cancellationToken);
    Task<QuoteCouponResponse> QuoteAsync(QuoteCouponRequest request, string accessToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    Task<EventOverview> ReadAsync(Guid day, string accessToken, CancellationToken cancellationToken);
}
public sealed record EventOverview(RaceDayInformation Day, RaceList Races, DateTimeOffset ReadAt);

public sealed class ReadOnlyBackend : IReadOnlyBackend
{
    private readonly GrpcChannel channel;
    private readonly ConnectionProfile profile;
    private readonly TimeSpan deadline;
    public ReadOnlyBackend(ConnectionProfile profile) : this(profile, TimeSpan.FromSeconds(10)) { }
    internal ReadOnlyBackend(ConnectionProfile profile, TimeSpan deadline)
    {
        this.deadline = deadline;
        this.profile = profile;
        channel = GrpcChannel.ForAddress(profile.Backend, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            },
            MaxReceiveMessageSize = 4 * 1024 * 1024,
            MaxRetryAttempts = 1
        });
    }
    public async Task<PingResponse> PingAsync(CancellationToken cancellationToken) =>
        await new SystemService.SystemServiceClient(channel).PingAsync(new PingRequest(),
            deadline: DateTime.UtcNow.Add(deadline), cancellationToken: cancellationToken);

    public async Task<EventOverview> ReadAsync(Guid day, string accessToken, CancellationToken cancellationToken)
    {
        if (!profile.KnownEvents.Contains(day)) throw new ArgumentException("Select a provisioned known event.");
        var headers = new Metadata { { "authorization", "Bearer " + accessToken } };
        var query = new DayQuery { RaceDayId = day.ToString("D") };
        var info = await new RaceDayService.RaceDayServiceClient(channel).GetRaceDayAsync(query, headers,
            DateTime.UtcNow.Add(deadline), cancellationToken);
        var racesClient = new RaceService.RaceServiceClient(channel);
        var races = await racesClient.ListRacesAsync(query, headers, DateTime.UtcNow.Add(deadline), cancellationToken);
        // A detail read is explicit per overview request, never a background polling loop.
        if (races.Races.Count > 0)
        {
            var first = races.Races[0];
            var detail = await racesClient.GetRaceAsync(new RaceQuery
            {
                RaceDayId = query.RaceDayId, RaceId = first.Definition.RaceId
            }, headers, DateTime.UtcNow.Add(deadline), cancellationToken);
            races.Races[0] = detail;
        }
        return new EventOverview(info, races, DateTimeOffset.UtcNow);
    }
    public async Task<QuoteCouponResponse> QuoteAsync(QuoteCouponRequest request, string accessToken, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.RaceDayId, out var day) || !profile.KnownEvents.Contains(day))
            throw new ArgumentException("Select a provisioned known event.");
        return await new BettingService.BettingServiceClient(channel).QuoteCouponAsync(request,
            new Metadata { { "authorization", "Bearer " + accessToken } },
            DateTime.UtcNow.Add(deadline), cancellationToken);
    }
    public void Dispose() => channel.Dispose();

    // Internal mutation transport prerequisite. Native UI may only reach this through a durable coordinator.
    // There is deliberately no retry policy or redirect fallback on the configured channel.
    internal async Task<byte[]> TransmitJournaledAsync(MutationRecord sending, string accessToken, CancellationToken cancellationToken)
    {
        if (sending.State != MutationState.Sending || sending.Binding.Profile != profile.Id ||
            sending.Binding.Backend != profile.Backend.OriginalString || sending.Binding.Issuer != profile.Issuer.OriginalString ||
            sending.Binding.Client != profile.ClientId || !Guid.TryParse(sending.Binding.Event, out var day) ||
            !profile.KnownEvents.Contains(day)) throw new InvalidOperationException("Journal transport context mismatch.");
        var headers = new Metadata { { "authorization", "Bearer " + accessToken } };
        if (sending.Method == "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon")
        {
            var request = PurchaseCouponRequest.Parser.ParseFrom(sending.Request);
            if (!request.ToByteArray().AsSpan().SequenceEqual(sending.Request))
                throw new InvalidOperationException("Original request cannot be reproduced exactly.");
            var response = await new BettingService.BettingServiceClient(channel).PurchaseCouponAsync(request, headers,
                DateTime.UtcNow.Add(deadline), cancellationToken);
            return response.ToByteArray();
        }
        if (sending.Method == "pl.umamusume.tiket.api.v1.PrintingService/ReprintCoupon")
        {
            var request = ReprintCouponRequest.Parser.ParseFrom(sending.Request);
            if (!request.ToByteArray().AsSpan().SequenceEqual(sending.Request))
                throw new InvalidOperationException("Original reprint cannot be reproduced exactly.");
            var response = await new PrintingService.PrintingServiceClient(channel).ReprintCouponAsync(request, headers,
                DateTime.UtcNow.Add(deadline), cancellationToken);
            return response.ToByteArray();
        }
        throw new InvalidOperationException("Unknown mutation method.");
    }

    internal async Task<PrintStatus> ReadPrintStatusAsync(string job, string accessToken, CancellationToken cancellationToken) =>
        await new PrintingService.PrintingServiceClient(channel).GetPrintStatusAsync(new PrintStatusQuery { PrintJobId = job },
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellationToken);
    internal async Task<AccountInformation> ReadAccountAsync(AccountQuery query, string accessToken, CancellationToken cancellationToken) =>
        await new AccountService.AccountServiceClient(channel).GetAccountAsync(query,
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellationToken);
}

public enum ConnectionState { Unconfigured, SignedOut, Discovering, BrowserPending, Validating, SignedIn, Connecting, Ready, Expired, Denied, Offline, Cancelled, Error, UnknownEvent, DiscoveryError, BrowserError, CallbackError, ValidationError }

public static class ConnectionErrors
{
    public static ConnectionState State(StatusCode code) => code switch
    {
        StatusCode.Unauthenticated => ConnectionState.Expired,
        StatusCode.PermissionDenied => ConnectionState.Denied,
        StatusCode.NotFound => ConnectionState.UnknownEvent,
        StatusCode.Unavailable or StatusCode.DeadlineExceeded => ConnectionState.Offline,
        StatusCode.Cancelled => ConnectionState.Cancelled,
        _ => ConnectionState.Error
    };
    public static string Message(ConnectionState state) => state switch
    {
        ConnectionState.DiscoveryError => "Issuer discovery failed. Check HTTPS certificate trust, reachability, exact issuer, and advertised Code/PKCE S256 support.",
        ConnectionState.BrowserError => "System browser could not open. Check the Windows default browser registration and try again.",
        ConnectionState.CallbackError => "Local callback failed or timed out. Check loopback availability and native redirect registration; then sign in again.",
        ConnectionState.ValidationError => "OIDC response rejected. Check client audience, issuer/JWKS, nonce, token lifetime and native registration.",
        ConnectionState.Unconfigured => "No managed profiles. Ask an administrator to provision connections.json.",
        ConnectionState.SignedOut => "Signed out. Sign in with the system browser.",
        ConnectionState.Discovering => "Checking trusted issuer discovery.",
        ConnectionState.BrowserPending => "Complete sign-in in your browser, or cancel.",
        ConnectionState.Validating => "Exchanging code and validating identity.",
        ConnectionState.SignedIn => "Signed in. Event access has not been checked.",
        ConnectionState.Connecting => "Reading the selected event.",
        ConnectionState.Ready => "Authorized server data loaded.",
        ConnectionState.Expired => "Session expired or token rejected. Sign in again; check API audience and RS256 JWT configuration.",
        ConnectionState.Denied => "Access denied for this event. Ask an administrator to check role and event grants.",
        ConnectionState.UnknownEvent => "The authorized event or race was not found.",
        ConnectionState.Offline => "Backend unavailable or request deadline exceeded. Check network and TLS trust.",
        ConnectionState.Cancelled => "Operation cancelled.",
        _ => "Connection or sign-in failed. Check trusted issuer, TLS trust, native client registration and PKCE S256 support."
    };
}
