using System.Buffers.Binary;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

/// <summary>Real HTTP/2 and gRPC wire frames, generated production clients; no production server code.</summary>
public class GrpcTransportTests
{
    [Fact]
    public async Task GeneratedClientsSendOnlyAccessTokenAndPreserveServerData()
    {
        await using var fixture = await GrpcFixture.Start();
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        var ping = await backend.PingAsync(CancellationToken.None);
        Assert.Equal("v1-fixture", ping.ApiVersion);
        var overview = await backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None);
        Assert.Equal(DayPhase.Running, overview.Day.Status.Phase);
        Assert.Equal(71, overview.Day.Status.Version);
        Assert.Equal("02", overview.Races.Races[0].Definition.Runners[0].ProgramNumber);
        Assert.Equal(RacePhase.BettingClosed, overview.Races.Races[0].Status.Phase);
        Assert.Equal(42, overview.Races.Races[0].Status.Version);
        Assert.Equal(4, fixture.Calls.Count);
        Assert.Empty(fixture.Calls[0].Auth);
        Assert.All(fixture.Calls.Skip(1), c => Assert.Equal(new[] { "Bearer access-only" }, c.Auth));
        Assert.All(fixture.Calls, c => { Assert.Equal("HTTP/2", c.Protocol); Assert.False(string.IsNullOrEmpty(c.Timeout)); });
        await Assert.ThrowsAsync<ArgumentException>(() => backend.ReadAsync(Guid.NewGuid(), "access-only", CancellationToken.None));
        Assert.Equal(4, fixture.Calls.Count);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task RealTrailersRetainStatus(StatusCode code)
    {
        await using var fixture = await GrpcFixture.Start(code);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None));
        Assert.Equal(code, error.StatusCode);
    }

    [Fact] public async Task CancellationReachesTransport()
    {
        await using var fixture = await GrpcFixture.Start(delay: true);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.PingAsync(cancel.Token));
        Assert.Equal(StatusCode.Cancelled, error.StatusCode);
    }

    [Fact] public async Task DeadlineActuallyExpires()
    {
        await using var fixture = await GrpcFixture.Start(delay: true);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true), TimeSpan.FromMilliseconds(150));
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.PingAsync(CancellationToken.None));
        Assert.Equal(StatusCode.DeadlineExceeded, error.StatusCode);
    }

    [Fact] public async Task RedirectDoesNotForwardBearerToAnotherOrigin()
    {
        await using var other = await GrpcFixture.Start();
        await using var origin = await GrpcFixture.Start(redirect: other.Address);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(origin.Address, development: true));
        await Assert.ThrowsAsync<RpcException>(() => backend.ReadAsync(ConnectionTests.Day, "access-only", CancellationToken.None));
        Assert.Single(origin.Calls);
        Assert.Empty(other.Calls);
    }

    [Fact] public async Task QuoteUsesExactMethodPayloadAndAuthoritativeResponse()
    {
        await using var fixture = await GrpcFixture.Start();
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        using var editor = QuoteTests.Editor();
        editor.Edit(editor.Draft with { Units = "9007199254740993" });
        var request = editor.Request();
        var reply = await backend.QuoteAsync(request, "access-only", CancellationToken.None);
        Assert.Equal("/pl.umamusume.tiket.api.v1.BettingService/QuoteCoupon", fixture.QuotePath);
        Assert.Equal(request.ToByteArray(), fixture.QuoteRequest!.ToByteArray());
        Assert.Equal(QuoteTests.Reply(), reply);
        Assert.Equal(new[] { "Bearer access-only" }, fixture.Calls.Single().Auth);
        Assert.Equal("HTTP/2", fixture.Calls.Single().Protocol);
        Assert.NotEmpty(fixture.Calls.Single().Timeout);
    }
    [Theory]
    [InlineData(StatusCode.Unauthenticated)] [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.InvalidArgument)] [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.ResourceExhausted)] [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Unavailable)] [InlineData(StatusCode.DeadlineExceeded)] [InlineData(StatusCode.Cancelled)]
    public async Task QuoteTrailersMapDistinctly(StatusCode code)
    {
        await using var fixture = await GrpcFixture.Start(code);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development: true));
        using var editor = QuoteTests.Editor();
        await editor.QuoteAsync((r,ct)=>backend.QuoteAsync(r,"access-only",ct));
        Assert.Equal(CashierComposer.Map(code),editor.State);
        Assert.Null(editor.Response);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task QuoteCancellationAndDeadline(bool cancel)
    {
        await using var fixture = await GrpcFixture.Start(delay:true);
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address, development:true), cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(150));
        using var ct = new CancellationTokenSource();
        if(cancel) ct.CancelAfter(150);
        using var editor = QuoteTests.Editor();
        var error=await Assert.ThrowsAsync<RpcException>(()=>backend.QuoteAsync(editor.Request(),"access-only",ct.Token));
        Assert.Equal(cancel ? StatusCode.Cancelled : StatusCode.DeadlineExceeded,error.StatusCode);
    }

    private static MutationRecord WireIntent(ConnectionProfile profile, bool reprint = false)
    {
        var coupon = Guid.NewGuid().ToString();
        var key = Guid.NewGuid().ToString();
        var player = Guid.NewGuid().ToString();
        var binding = new MutationBinding(profile.Id, profile.Backend.OriginalString, profile.Issuer.OriginalString,
            profile.ClientId, "synthetic-subject", ConnectionTests.Day.ToString(), 1);
        var request = new PurchaseCouponRequest { RaceDayId = binding.Event, RaceId = Guid.NewGuid().ToString(),
            PlayerId = player, CouponId = coupon, IdempotencyKey = key, PointOfSaleNumber = 1,
            CashierIdentifier = "fixture-cashier", QuoteConfirmation = "opaque" };
        return new MutationRecord(1, Guid.NewGuid(), binding,
            reprint ? "pl.umamusume.tiket.api.v1.PrintingService/ReprintCoupon" : "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon",
            reprint ? new ReprintCouponRequest { RaceDayId = binding.Event, CouponId = coupon, IdempotencyKey = key }.ToByteArray() : request.ToByteArray(),
            coupon, key, player, request.CashierIdentifier, new QuoteCouponResponse { Pricing = QuoteTests.Reply().Pricing }.ToByteArray(),
            DateTimeOffset.UtcNow, MutationState.Sending, null, reprint ? Guid.NewGuid() : null);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MutationUsesGeneratedClientExactPayloadAndOneBearer(bool reprint)
    {
        await using var fixture = await GrpcFixture.Start();
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var backend = new ReadOnlyBackend(profile);
        var intent = WireIntent(profile, reprint);
        var bytes = await backend.TransmitJournaledAsync(intent, "access-only", CancellationToken.None);
        Assert.Equal("/" + intent.Method, fixture.MutationPath);
        Assert.Equal(intent.Request, fixture.MutationRequest);
        Assert.Equal(new[] { "Bearer access-only" }, fixture.Calls.Single().Auth);
        Assert.Equal("HTTP/2", fixture.Calls.Single().Protocol);
        Assert.NotEmpty(fixture.Calls.Single().Timeout);
        if (reprint) Assert.Equal(PrintJobType.Reprint, PrintStatus.Parser.ParseFrom(bytes).Type);
        else Assert.Equal(intent.Coupon, PurchaseCouponResponse.Parser.ParseFrom(bytes).CouponId);
        var status = await backend.ReadPrintStatusAsync("fixture-job", "access-only", CancellationToken.None);
        Assert.Equal(DispatchStatus.Dispatched, status.Status);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)] [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.InvalidArgument)] [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.AlreadyExists)] [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.FailedPrecondition)] [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Unavailable)] [InlineData(StatusCode.DeadlineExceeded)] [InlineData(StatusCode.Cancelled)]
    public async Task MutationErrorsPreserveStatusAndNeverAutomaticallyRetry(StatusCode code)
    {
        await using var fixture = await GrpcFixture.Start(code);
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var backend = new ReadOnlyBackend(profile);
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.TransmitJournaledAsync(
            WireIntent(profile), "access-only", CancellationToken.None));
        Assert.Equal(code, error.StatusCode);
        Assert.Single(fixture.Calls);
        Assert.DoesNotContain("access-only", MutationMessages.For(code));
    }

    [Fact]
    public async Task MutationRejectsPreparedAndRepointedContextBeforeNetwork()
    {
        await using var fixture = await GrpcFixture.Start();
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var backend = new ReadOnlyBackend(profile);
        var intent = WireIntent(profile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.TransmitJournaledAsync(
            intent with { State = MutationState.Prepared }, "access-only", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.TransmitJournaledAsync(
            intent with { Binding = intent.Binding with { Backend = "https://other.invalid" } }, "access-only", CancellationToken.None));
        Assert.Empty(fixture.Calls);
    }

    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task MutationCancellationAndDeadlineDoNotRetry(bool reprint, bool cancel)
    {
        await using var fixture = await GrpcFixture.Start(delay: true);
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var backend = new ReadOnlyBackend(profile, cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(150));
        using var token = new CancellationTokenSource(); if (cancel) token.CancelAfter(150);
        var error = await Assert.ThrowsAsync<RpcException>(() => backend.TransmitJournaledAsync(WireIntent(profile, reprint), "access-only", token.Token));
        Assert.Equal(cancel ? StatusCode.Cancelled : StatusCode.DeadlineExceeded, error.StatusCode);
        // On a loaded runner the budget can expire before HTTP/2 setup completes; neither path may retry.
        Assert.InRange(fixture.Calls.Count, 0, 1);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MutationRedirectCannotForwardBearerOrRequest(bool reprint)
    {
        await using var other = await GrpcFixture.Start();
        await using var origin = await GrpcFixture.Start(redirect: other.Address);
        var profile = ConnectionTests.Profile(origin.Address, development: true);
        using var backend = new ReadOnlyBackend(profile);
        await Assert.ThrowsAsync<RpcException>(() => backend.TransmitJournaledAsync(WireIntent(profile, reprint), "access-only", CancellationToken.None));
        Assert.Single(origin.Calls); Assert.Empty(other.Calls);
    }

    internal sealed class GrpcFixture(WebApplication app) : IAsyncDisposable
    {
        public string? AdministrationPath;
        public byte[]? AdministrationRequest;
        public string? QuotePath;
        public QuoteCouponRequest? QuoteRequest;
        public string? MutationPath;
        public byte[]? MutationRequest;
        public TaskCompletionSource<bool>? PurchaseGate;
        public TaskCompletionSource<bool> PurchaseReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Address => app.Urls.Single();
        public List<(string[] Auth, string Timeout, string Protocol)> Calls = [];
        public static async Task<GrpcFixture> Start(StatusCode? error = null, bool delay = false, string? redirect = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => o.Listen(System.Net.IPAddress.Loopback, 0, p => p.Protocols = HttpProtocols.Http2));
            var app = builder.Build();
            var fixture = new GrpcFixture(app);
            app.Run(async context =>
            {
                fixture.Calls.Add((context.Request.Headers.Authorization.Select(s => s!).ToArray(),
                    context.Request.Headers["grpc-timeout"].ToString(), context.Request.Protocol));
                if (redirect is not null) { context.Response.StatusCode = 307; context.Response.Headers.Location = redirect + context.Request.Path; return; }
                context.Response.ContentType = "application/grpc";
                if (delay) { await Task.Delay(30000, context.RequestAborted); return; }
                if (error is not null)
                {
                    context.Response.Headers["grpc-status"] = ((int)error).ToString();
                    return;
                }
                using var input = new MemoryStream();
                await context.Request.Body.CopyToAsync(input);
                var wire = input.ToArray();
                var method = context.Request.Path.Value!.Split('/').Last();
                IMessage result;
                if (method == "Ping") result = new PingResponse { Status = "OK", ApiVersion = "v1-fixture" };
                else if (method == "QuoteCoupon")
                {
                    fixture.QuotePath = context.Request.Path;
                    fixture.QuoteRequest = QuoteCouponRequest.Parser.ParseFrom(wire.AsSpan(5));
                    result = QuoteTests.Reply();
                }
                else if (method == "PurchaseCoupon")
                {
                    fixture.MutationPath = context.Request.Path;
                    fixture.MutationRequest = wire.AsSpan(5).ToArray();
                    var purchase = PurchaseCouponRequest.Parser.ParseFrom(fixture.MutationRequest);
                    fixture.PurchaseReceived.TrySetResult(true);
                    if (fixture.PurchaseGate is not null) await fixture.PurchaseGate.Task;
                    var purchasedPricing = QuoteTests.Reply().Pricing;
                    purchasedPricing.RaceDayId = purchase.RaceDayId; purchasedPricing.RaceId = purchase.RaceId;
                    result = new PurchaseCouponResponse { CouponId = purchase.CouponId, PlayerId = purchase.PlayerId,
                        Pricing = purchasedPricing, PrintJobId = Guid.NewGuid().ToString(), PrintingStatus = DispatchStatus.Pending };
                }
                else if (method == "ReprintCoupon")
                {
                    fixture.MutationPath = context.Request.Path;
                    fixture.MutationRequest = wire.AsSpan(5).ToArray();
                    var reprint = ReprintCouponRequest.Parser.ParseFrom(fixture.MutationRequest);
                    result = new PrintStatus { CouponId = reprint.CouponId, PrintJobId = "fixture-reprint",
                        Type = PrintJobType.Reprint, Status = DispatchStatus.Pending };
                }
                else if (method is "CreateRaceDay" or "PublishRaceDay" or "StartRaceDay" or "OpenBetting" or "CloseBetting" or "EnrollPlayer" or "AddOpeningFunding" or "GetEnrollment" or "ListPlayers" or "GetAccount")
                {
                    fixture.AdministrationPath = context.Request.Path;
                    fixture.AdministrationRequest = wire.AsSpan(5).ToArray();
                    var day = ConnectionTests.Day.ToString();
                    var player = QuoteTests.Horses[0];
                    result = method switch {
                        "CreateRaceDay" => new DayAcknowledgement { RaceDayId = day, Phase = DayPhase.Draft },
                        "PublishRaceDay" => new DayAcknowledgement { RaceDayId = day, Phase = DayPhase.Ready, Version = 8 },
                        "StartRaceDay" => new DayAcknowledgement { RaceDayId = day, Phase = DayPhase.Running, Version = 8 },
                        "OpenBetting" => new RaceAcknowledgement { RaceDayId = day, RaceId = QuoteTests.RaceId, Phase = RacePhase.BettingOpen, Version = 14 },
                        "CloseBetting" => new RaceAcknowledgement { RaceDayId = day, RaceId = QuoteTests.RaceId, Phase = RacePhase.BettingClosed, Version = 14 },
                        "EnrollPlayer" or "AddOpeningFunding" or "GetEnrollment" => new EnrollmentInformation { RaceDayId = day, PlayerId = player, OpeningPaid = 31, OpeningPromotional = 7, Balance = 38, AccountVersion = 6 },
                        "GetAccount" => new AccountInformation { RaceDayId = day, PlayerId = player, Balance = 38, Currency = "PLN", AccountVersion = 6 },
                        _ => new PlayerList { Players = { new EnrollmentInformation { RaceDayId = day, PlayerId = player, Balance = 38, AccountVersion = 6 } } }
                    };
                }
                else if (method is "ScratchHorse" or "VoidRace" or "SubmitResult" or "VerifyResult" or "SettleRace")
                {
                    fixture.AdministrationPath = context.Request.Path;
                    fixture.AdministrationRequest = wire.AsSpan(5).ToArray();
                    result = Phase6Tests.Reply(context.Request.Path.Value!.TrimStart('/'), fixture.AdministrationRequest);
                }
                else if (method is "CashOut" or "FinalizeRaceDay" or "CompleteRaceDay" or "CloseRaceDay" or "GetWithdrawalQuote" or "GetCashoutReceipt" or "ListAccountMovements") {
                    fixture.AdministrationPath = context.Request.Path;
                    fixture.AdministrationRequest = wire.AsSpan(5).ToArray();
                    if (method == "CashOut") {
                        var c = CashOutRequest.Parser.ParseFrom(fixture.AdministrationRequest);
                        result = new CashoutReceipt { RaceDayId=c.RaceDayId,PlayerId=c.PlayerId,AccountVersion=c.ExpectedAccountVersion+1,Currency="PLN",CashedOutAt="timestamp",ReceiptReference="receipt" };
                    } else result = method switch {
                        "GetWithdrawalQuote" => new WithdrawalQuoteInformation(),
                        "GetCashoutReceipt" => new CashoutReceipt(),
                        "ListAccountMovements" => new AccountMovements(),
                        _ => new DayAcknowledgement { RaceDayId=DayCommand.Parser.ParseFrom(fixture.AdministrationRequest).RaceDayId,
                            Version=DayCommand.Parser.ParseFrom(fixture.AdministrationRequest).ExpectedVersion+1,
                            Phase=method=="FinalizeRaceDay"?DayPhase.Finalizing:method=="CompleteRaceDay"?DayPhase.Completed:DayPhase.Closed }
                    };
                }
                else if (method is "GetResult" or "GetSettlement" or "ListRaceLegResolutions" or "GetCoupon")
                {
                    fixture.AdministrationPath = context.Request.Path;
                    fixture.AdministrationRequest = wire.AsSpan(5).ToArray();
                    result = method switch {
                        "GetResult" => new ResultAcknowledgement(),
                        "GetSettlement" => new SettlementInformation(),
                        "ListRaceLegResolutions" => new RaceLegResolutions(),
                        _ => new CouponInformation()
                    };
                }
                else if (method == "GetPrintStatus") result = new PrintStatus { PrintJobId = "fixture-job",
                    Type = PrintJobType.Original, Status = DispatchStatus.Dispatched };
                else
                {
                    var query = method == "GetRace" ? RaceQuery.Parser.ParseFrom(wire.AsSpan(5)).RaceDayId : DayQuery.Parser.ParseFrom(wire.AsSpan(5)).RaceDayId;
                    Assert.Equal(ConnectionTests.Day.ToString(), query);
                    var race = new RaceInformation
                    {
                        Definition = new RaceConfiguration { RaceId = "ad238129-b219-4a6e-908f-a5da6c73e932", Runners = { new Runner { HorseId = "horse", ProgramNumber = "02" } } },
                        Status = new RaceAcknowledgement { Phase = RacePhase.BettingClosed, Version = 42 }
                    };
                    result = method switch
                    {
                        "GetRaceDay" => new RaceDayInformation { Definition = new RaceDayConfiguration { RaceDayId = query }, Status = new DayAcknowledgement { Phase = DayPhase.Running, Version = 71 } },
                        "ListRaces" => new RaceList { Races = { race } },
                        "GetRace" => race,
                        _ => throw new InvalidOperationException("Unexpected RPC")
                    };
                }
                var bytes = result.ToByteArray();
                var frame = new byte[bytes.Length + 5];
                BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), bytes.Length);
                bytes.CopyTo(frame, 5);
                context.Response.DeclareTrailer("grpc-status");
                await context.Response.Body.WriteAsync(frame);
                context.Response.AppendTrailer("grpc-status", "0");
            });
            await app.StartAsync();
            return fixture;
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
