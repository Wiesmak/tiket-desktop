using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;
namespace Tiket.Desktop.Tests;

public class Phase6Tests
{
    internal static AdministrationRecord Record(string operation)
    {
        var c = new RaceCommand { RaceDayId = ConnectionTests.Day.ToString(), RaceId = QuoteTests.RaceId,
            ExpectedDayVersion = 7, ExpectedRaceVersion = 13, IdempotencyKey = Guid.NewGuid().ToString() };
        IMessage request = operation switch {
            "ScratchHorse" => new ScratchHorseRequest { Command = c, HorseId = QuoteTests.Horses[0] },
            "VoidRace" => c,
            "SubmitResult" => new SubmitResultRequest { Command = c, Revision = "r1", Kind = OfficialResultKind.Normal, FinisherIds = { QuoteTests.Horses.Take(3) } },
            "VerifyResult" => new VerifyResultRequest { Command = c, Revision = "r1" },
            _ => new SettleRaceRequest { Command = c, SettlementId = Guid.NewGuid().ToString() }
        };
        return new(1, Guid.NewGuid(), new("test","http://backend","https://issuer","client","actor",c.RaceDayId),
            AdministrationWire.Prefix + (operation is "ScratchHorse" or "VoidRace" ? "RaceService/" : "ResultService/") + operation,
            request.ToByteArray(), c.IdempotencyKey, DateTimeOffset.UtcNow, MutationState.Prepared, null);
    }
    internal static IMessage Reply(string method, byte[] bytes)
    {
        var request = Phase6Wire.Parse(method, bytes); var c = Phase6Wire.Command(request);
        var status = new RaceAcknowledgement { RaceDayId = c.RaceDayId, RaceId = c.RaceId, Version = c.ExpectedRaceVersion + 1, SelectedResultRevision = "r1" };
        if (request is ScratchHorseRequest scratch) { status.ScratchedHorseIds.Add(scratch.HorseId); status.Phase = RacePhase.BettingClosed; return new RefundAcknowledgement { Status = status, RefundedLegs = 1, RefundedStake = 10 }; }
        if (request is RaceCommand) { status.Phase = RacePhase.Voided; return new RefundAcknowledgement { Status = status }; }
        if (request is SettleRaceRequest settle) { status.Phase = RacePhase.Settled; return new SettlementInformation { Status = status, SettlementId = settle.SettlementId, ResultRevision = "r1" }; }
        status.Phase = request is SubmitResultRequest ? RacePhase.ResultProposed : RacePhase.ResultVerified;
        return new ResultAcknowledgement { Status = status, Result = new OfficialResult { RaceDayId = c.RaceDayId, RaceId = c.RaceId, Revision = "r1",
            Kind = OfficialResultKind.Normal, Status = request is SubmitResultRequest ? OfficialResultStatus.Proposed : OfficialResultStatus.Verified,
            FinisherIds = { QuoteTests.Horses.Take(3) } } };
    }
    [Theory] [InlineData("ScratchHorse")] [InlineData("VoidRace")] [InlineData("SubmitResult")] [InlineData("VerifyResult")] [InlineData("SettleRace")]
    public async Task NewGeneratedMutationsUseExactPayloadAndValidateAck(string operation)
    {
        await using var fixture = await GrpcTransportTests.GrpcFixture.Start();
        var profile = ConnectionTests.Profile(fixture.Address, development:true);
        using var backend = new ReadOnlyBackend(profile);
        var r = Record(operation);
        r = r with { State = MutationState.Sending, Binding = new(profile.Id,profile.Backend.OriginalString,profile.Issuer.OriginalString,profile.ClientId,"actor",r.Binding.Event) };
        var response = await ((IAdministrationBackend)backend).TransmitAdministrationAsync(r,"access-only",default);
        Assert.Equal("/" + r.Method, fixture.AdministrationPath); Assert.Equal(r.Request,fixture.AdministrationRequest);
        Assert.Equal(new[]{"Bearer access-only"},fixture.Calls.Single().Auth);
        AdministrationWire.Verify(r,response);
        Assert.ThrowsAny<Exception>(() => AdministrationWire.Verify(r,new RaceAcknowledgement().ToByteArray()));
    }
    [Theory] [InlineData("SubmitResult")] [InlineData("SettleRace")]
    public async Task UnknownReopensAndReplaysSameSubjectKeyAndSerializedRequest(string operation)
    {
        var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); var r = Record(operation);
        try {
            using(var journal = new AdministrationJournal(dir)) {
                journal.Prepare(r);
                await Assert.ThrowsAsync<MutationUncertainException>(() => new JournaledAdministration(journal).SendAsync(r.Id,r.Binding,
                    (_,_) => throw new RpcException(new Status(StatusCode.DeadlineExceeded,"")),default));
                Assert.Equal(MutationState.Unknown,journal.Read(r.Binding).Single().State);
            }
            using(var journal = new AdministrationJournal(dir)) {
                Assert.Empty(journal.Read(r.Binding with { Subject = "other" }));
                var ack = await new JournaledAdministration(journal).SendAsync(r.Id,r.Binding,(saved,_) => {
                    Assert.Equal(r.Request,saved.Request); Assert.Equal(r.Key,saved.Key);
                    return Task.FromResult(Reply(r.Method,saved.Request).ToByteArray());
                },default);
                Assert.Equal(MutationState.Acked,ack.State);
            }
        } finally { Directory.Delete(dir,true); }
    }
    [Theory] [InlineData("GetResult")] [InlineData("GetSettlement")] [InlineData("ListRaceLegResolutions")] [InlineData("GetCoupon")]
    public async Task NewGeneratedReadsUseExactScopedQuery(string method)
    {
        await using var fixture = await GrpcTransportTests.GrpcFixture.Start();
        using var backend = new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address,development:true));
        var transport = (IResultsBackend)backend;
        var race = new RaceQuery { RaceDayId = ConnectionTests.Day.ToString(), RaceId = QuoteTests.RaceId };
        IMessage request;
        switch(method) {
            case "GetResult": request = race; await transport.GetResultAsync(race,"access-only",default); break;
            case "GetSettlement":
                var settlement = new SettlementQuery { RaceDayId = race.RaceDayId, RaceId = race.RaceId, SettlementId = Guid.NewGuid().ToString() };
                request = settlement; await transport.GetSettlementAsync(settlement,"access-only",default); break;
            case "ListRaceLegResolutions":
                var page = new RaceLegResolutionsRequest { Race = race, Offset = 500, PageSize = 500 };
                request = page; await transport.ListResolutionsAsync(page,"access-only",default); break;
            default:
                var coupon = new CouponQuery { RaceDayId = race.RaceDayId, CouponId = Guid.NewGuid().ToString() };
                request = coupon; await transport.GetWinningCouponAsync(coupon,"access-only",default); break;
        }
        Assert.Equal("/" + AdministrationWire.Prefix + (method == "GetCoupon" ? "BettingService/" : "ResultService/") + method,fixture.AdministrationPath);
        Assert.Equal(request.ToByteArray(),fixture.AdministrationRequest); Assert.Equal(new[]{"Bearer access-only"},fixture.Calls.Single().Auth);
        Assert.NotEmpty(fixture.Calls.Single().Timeout);
    }
    [Theory] [InlineData("SubmitResult","RESULT_SUBMITTER")] [InlineData("VerifyResult","RESULT_VERIFIER")] [InlineData("SettleRace","SETTLEMENT_OPERATOR")] [InlineData("VoidRace","EVENT_ADMIN")]
    public void DistinctSignedRoleMapping(string operation,string role) => Assert.Equal(role,Phase6Wire.RequiredRole(Record(operation).Method));

    [Theory] [InlineData(false)] [InlineData(true)]
    public void WinnersSeparateLegAndCouponStakeExcludeRefundsAndFlagDeniedCoupons(bool denied)
    {
        var s = new SettlementInformation { SettlementId = "settlement", Pools = { new PoolSettlementSummary { Type = PoolType.Win, Distributed = 9 } },
            Players = { new PlayerFinancialSummary { PlayerId = "player", Amount = 9 } } };
        var legs = new[] { new TerminalLegResolution { CouponId = "coupon", Ordinal = 1, Outcome = TerminalLegOutcome.Won, Amount = 9, SettlementId = s.SettlementId },
            new TerminalLegResolution { CouponId = "refund", Ordinal = 2, Outcome = TerminalLegOutcome.Refunded, Amount = 10 } };
        var coupons = new Dictionary<string,CouponInformation>();
        if(!denied) coupons["coupon"] = new() { CouponId = "coupon", PlayerId = "player", Pricing = new() { RaceDayId = "day", RaceId = "race", Currency = "PLN", BetType = PoolType.Win, TotalStake = 30 },
            Legs = { new PurchasedLeg { Ordinal = 1, Stake = 10, HorseIds = { "horse" } } } };
        var report = WinnersReport.Build("day","race","PLN",legs,coupons,s,!denied);
        Assert.Equal(!denied,report.Complete);
        if(!denied) { var row = Assert.Single(report.Rows); Assert.Equal(10,row.LegStake); Assert.Equal(30,row.CouponStake); Assert.Equal(9,row.Award); }
        else Assert.Empty(report.Rows);
    }
    [Fact] public void ZeroWinnersAndUnavailableSettlementAreDistinct()
    {
        Assert.True(WinnersReport.Build("d","r","PLN",[],new Dictionary<string,CouponInformation>(),new(),true).Complete);
        Assert.Equal("Settlement details unavailable",WinnersReport.Build("d","r","PLN",[],new Dictionary<string,CouponInformation>(),null,true).Status);
    }
    private sealed class Login(string[] roles, bool eventGrant = true) : INativeLogin {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) =>
            Task.FromResult(new LoginMaterial("actor","synthetic",DateTimeOffset.UtcNow.AddMinutes(5)) {
                AdministrationGrants = new("actor",roles,eventGrant ? [ConnectionTests.Day.ToString()] : [],[])
            });
    }
    private sealed class Server : IReadOnlyBackend, IResultsBackend {
        public RaceInformation Race = new() { Definition = AdministrationSessionTests.Definition().Races[0].Clone(),
            Status = new() { RaceDayId = ConnectionTests.Day.ToString(), RaceId = QuoteTests.RaceId, Phase = RacePhase.BettingClosed, Version = 13 } };
        public int Pages;
        public bool BadPage;
        public Task<EventOverview> ReadAsync(Guid id,string t,CancellationToken ct) => Task.FromResult(new EventOverview(
            new() { Definition = AdministrationSessionTests.Definition(), Status = new() { Phase = DayPhase.Running, Version = 7 } },
            new() { Races = { Race.Clone() } },DateTimeOffset.UtcNow));
        public Task<PingResponse> PingAsync(CancellationToken ct) => Task.FromResult(new PingResponse());
        public Task<ResultAcknowledgement> GetResultAsync(RaceQuery q,string t,CancellationToken ct) => Task.FromResult(new ResultAcknowledgement {
            Status = Race.Status.Clone(), Result = new() { RaceDayId = q.RaceDayId, RaceId = q.RaceId, Revision = "r1" } });
        public Task<RaceLegResolutions> ListResolutionsAsync(RaceLegResolutionsRequest q,string t,CancellationToken ct) {
            Pages++; Assert.InRange(q.PageSize,1,500);
            return Task.FromResult(q.Offset == 0 ? new RaceLegResolutions { NextOffset = BadPage ? 0 : 1 } : new RaceLegResolutions());
        }
        public Task<SettlementInformation> GetSettlementAsync(SettlementQuery q,string t,CancellationToken ct) => throw new Exception("No fabricated settlement IDs");
        public Task<CouponInformation> GetWinningCouponAsync(CouponQuery q,string t,CancellationToken ct) => throw new Exception("No wins");
        public void Dispose() { }
    }
    [Theory] [InlineData("RESULT_SUBMITTER",true)] [InlineData("EVENT_ADMIN",false)] [InlineData("CASHIER",false)]
    public async Task SubmitMappingUsesRealEligibleHorsesVersionsAndDistinctRole(string role,bool allowed)
    {
        var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try {
            using var journal = new AdministrationJournal(dir);
            var server = new Server();
            using var session = new ConnectionSession(new Login([role]),_ => server);
            session.UseAdministrationStorage(journal,Path.Combine(dir,"drafts"));
            session.Select(ConnectionTests.Profile()); await session.SignInAsync(); await session.SynchronizeAsync(ConnectionTests.Day,default);
            if(!allowed) { Assert.Throws<InvalidOperationException>(() => session.ReviewResultOperation(AdministrationOperation.SubmitResult,QuoteTests.RaceId,"r1",finishers:QuoteTests.Horses.Take(3))); return; }
            Assert.Throws<InvalidOperationException>(() => session.ReviewResultOperation(AdministrationOperation.SubmitResult,QuoteTests.RaceId,"r1",finishers:QuoteTests.Horses.Take(2)));
            Assert.Throws<InvalidOperationException>(() => session.ReviewResultOperation(AdministrationOperation.SubmitResult,QuoteTests.RaceId,"r1",finishers:Enumerable.Repeat(QuoteTests.Horses[0],3)));
            var review = session.ReviewResultOperation(AdministrationOperation.SubmitResult,QuoteTests.RaceId,"r1",finishers:QuoteTests.Horses.Take(3));
            Assert.Contains("expectedRaceVersion",review.Payload); Assert.Contains("13",review.Payload);
            server.Race.Status.Version++; await session.SynchronizeAsync(ConnectionTests.Day,default);
            Assert.False(session.IsAdministrationReviewCurrent(review.Id));
            session.CancelAdministrationReview(); server.Race.Status.ScratchedHorseIds.Add(QuoteTests.Horses[0]);
            await session.SynchronizeAsync(ConnectionTests.Day,default);
            Assert.Throws<InvalidOperationException>(() => session.ReviewResultOperation(AdministrationOperation.SubmitResult,QuoteTests.RaceId,"r2",finishers:QuoteTests.Horses.Take(3)));
        } finally { Directory.Delete(dir,true); }
    }
    [Theory] [InlineData(true,false)] [InlineData(true,true)] [InlineData(false,false)]
    public async Task ReportPaginatesAndCashierCannotReadGlobalWinners(bool auditor,bool badPage)
    {
        var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try {
            using var journal = new AdministrationJournal(dir);
            var server = new Server { BadPage = badPage };
            server.Race.Status.Phase = RacePhase.Settled; server.Race.Status.SelectedResultRevision = "r1";
            using var session = new ConnectionSession(new Login(auditor ? ["AUDITOR"] : ["CASHIER"]),_ => server);
            session.UseAdministrationStorage(journal,Path.Combine(dir,"drafts"));
            session.Select(ConnectionTests.Profile()); await session.SignInAsync(); await session.SynchronizeAsync(ConnectionTests.Day,default);
            session.SelectResultRace(QuoteTests.RaceId); Assert.True(await session.ReadResultsAsync(default));
            Assert.Equal(auditor ? (badPage ? 1 : 2) : 0,server.Pages);
            if(auditor) { Assert.False(session.Winners!.Complete); Assert.Equal("Settlement details unavailable",session.Winners.Status); }
            else { Assert.Null(session.Winners); Assert.Contains("Auditor access required",session.ResultsStatus); }
        } finally { Directory.Delete(dir,true); }
    }
}
