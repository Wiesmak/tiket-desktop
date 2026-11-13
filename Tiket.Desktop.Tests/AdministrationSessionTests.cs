using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class AdministrationSessionTests
{
    internal static RaceDayConfiguration Definition() => new() {
        RaceDayId = ConnectionTests.Day.ToString(), EventDate = "2028-02-29", DisplayNumber = 999, Currency = "PLN",
        TimeZone = "Europe/Warsaw", RulesVersion = "1", MaximumCouponLegs = 10000, MaximumOpenRaces = 1,
        NoWinnerPolicy = NoWinnerPolicy.HouseRetainsPool, Races = {
            new RaceConfiguration { RaceId = QuoteTests.RaceId, Number = 1, Title = "First", RacecourseName = "Course",
                Runners = { QuoteTests.Horses.Take(3).Select((id,i) => new Runner { HorseId=id, ProgramNumber=(i+1).ToString("00"), DisplayName="Horse" }) },
                Pools = { new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }.Select(p=>new PoolConfiguration { Type=p, UnitStake=long.MaxValue, TakeBasisPoints=10000 }) } }
        }
    };
    private sealed class Login(string[] roles, bool player = true) : INativeLogin {
        public string Subject = "actor";
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) =>
            Task.FromResult(new LoginMaterial(Subject, "synthetic-bearer", DateTimeOffset.UtcNow.AddMinutes(5)) {
                AdministrationGrants = new(Subject, roles, [ConnectionTests.Day.ToString()], player ? [QuoteTests.Horses[0]] : [])
            });
    }
    private sealed class Server : IReadOnlyBackend, IAdministrationBackend, IAccountBackend {
        public RaceDayInformation? Day;
        public RaceList Races = new();
        public int Calls;
        public StatusCode? Reject;
        public TaskCompletionSource<byte[]>? Delayed;
        public EnrollmentInformation? Enrollment;
        public AccountInformation? Account;
        public byte[]? LastRequest;
        public string? LastKey;
        private readonly Dictionary<string,byte[]> acks = [];
        public Task<PingResponse> PingAsync(CancellationToken ct) => Task.FromResult(new PingResponse());
        public Task<EventOverview> ReadAsync(Guid day,string token,CancellationToken ct) => Day is null ?
            Task.FromException<EventOverview>(new RpcException(new Status(StatusCode.NotFound,""))) :
            Task.FromResult(new EventOverview(Day.Clone(), Races.Clone(), DateTimeOffset.UtcNow));
        public Task<byte[]> TransmitAdministrationAsync(AdministrationRecord saved,string token,CancellationToken ct) {
            Calls++; Assert.Equal(MutationState.Sending,saved.State);
            LastRequest=saved.Request.ToArray();LastKey=saved.Key;
            if(Delayed is not null)return Delayed.Task;
            if(Reject is {} code)throw new RpcException(new Status(code,"not displayed"));
            if(acks.TryGetValue(saved.Key,out var ack))return Task.FromResult(ack.ToArray());
            IMessage result;
            switch(saved.Method.Split('/').Last()) {
                case "CreateRaceDay":
                    var create=CreateRaceDayRequest.Parser.ParseFrom(saved.Request);
                    Day=new() { Definition=create.Definition.Clone(), Status=new() { RaceDayId=create.Definition.RaceDayId, Phase=DayPhase.Draft } };
                    Races=new() { Races={create.Definition.Races.Select(r=>new RaceInformation { Definition=r.Clone(),Status=new() { RaceDayId=Day.Definition.RaceDayId,RaceId=r.RaceId,Phase=RacePhase.Scheduled } })} };
                    result=Day.Status;break;
                case "PublishRaceDay": Day!.Status.Phase=DayPhase.Ready;Day.Status.Version++;result=Day.Status;break;
                case "StartRaceDay": Day!.Status.Phase=DayPhase.Running;Day.Status.Version++;result=Day.Status;break;
                case "OpenBetting":case "CloseBetting":
                    var race=Races.Races.Single(r=>r.Definition.RaceId==RaceCommand.Parser.ParseFrom(saved.Request).RaceId);
                    race.Status.Phase=saved.Method.EndsWith("OpenBetting")?RacePhase.BettingOpen:RacePhase.BettingClosed;
                    race.Status.Version++;result=race.Status;break;
                case "EnrollPlayer":
                    var enroll=EnrollPlayerRequest.Parser.ParseFrom(saved.Request);
                    Enrollment=new() { RaceDayId=enroll.RaceDayId,PlayerId=enroll.PlayerId,OpeningPaid=enroll.OpeningPaid,OpeningPromotional=enroll.OpeningPromotional,Balance=enroll.OpeningPaid+enroll.OpeningPromotional };
                    Account=new() { RaceDayId=enroll.RaceDayId,PlayerId=enroll.PlayerId,Currency="PLN",Balance=Enrollment.Balance };
                    result=Enrollment;break;
                default:
                    var fund=AddOpeningFundingRequest.Parser.ParseFrom(saved.Request);
                    Enrollment!.OpeningPaid+=fund.AdditionalPaid;Enrollment.OpeningPromotional+=fund.AdditionalPromotional;
                    Enrollment.Balance+=fund.AdditionalPaid+fund.AdditionalPromotional;Enrollment.AccountVersion++;
                    Account!.Balance=Enrollment.Balance;Account.AccountVersion=Enrollment.AccountVersion;
                    result=Enrollment;break;
            }
            var bytes=result.ToByteArray();acks[saved.Key]=bytes;return Task.FromResult(bytes);
        }
        public Task<EnrollmentInformation> GetEnrollmentAsync(EnrollmentQuery query,string token,CancellationToken ct) =>
            Enrollment is {} value ? Task.FromResult(value.Clone()):Task.FromException<EnrollmentInformation>(new RpcException(new Status(StatusCode.NotFound,"")));
        public Task<AccountInformation> GetAccountAsync(AccountQuery query,string token,CancellationToken ct)=>Task.FromResult(Account!.Clone());
        public Task<PlayerList> ListPlayersAsync(DayQuery query,string token,CancellationToken ct)=>Task.FromResult(new PlayerList { Players={Enrollment is null?[]:new[]{Enrollment.Clone()}} });
        public void Dispose() {}
    }
    private sealed class Fixture : IDisposable {
        public readonly string Directory=Path.Combine(Path.GetTempPath(),"tiket-admin-"+Guid.NewGuid());
        public readonly Server Server=new();
        public readonly Login Login;
        public readonly ConnectionSession Session;
        public readonly AdministrationJournal Journal;
        public Fixture(string[]? roles=null,bool player=true) {
            Login=new(roles??["EVENT_ADMIN","CASHIER","AUDITOR"],player);
            Journal=new AdministrationJournal(Path.Combine(Directory,"journal"));
            Session=new(Login,_=>Server);Session.UseAdministrationStorage(Journal,Path.Combine(Directory,"drafts"));
            Session.Select(ConnectionTests.Profile());
        }
        public async Task Ready() { await Session.SignInAsync();Session.SelectEvent(ConnectionTests.Day);await Session.SynchronizeAsync(ConnectionTests.Day,default); }
        public async Task Create() {
            await Ready();var review=Session.ReviewCreate(new(Definition()));
            await Session.ConfirmAdministrationAsync(review.Id);await Session.SynchronizeAsync(ConnectionTests.Day,default);
        }
        public async Task Confirm(AdministrationReview review) { await Session.ConfirmAdministrationAsync(review.Id);await Session.SynchronizeAsync(ConnectionTests.Day,default); }
        public void Dispose() { Session.Dispose(); System.IO.Directory.Delete(Directory,true); }
    }
    [Fact] public async Task NativeSessionSupportsEveryPhase5CommandAndIndependentAuditorRead() {
        using var f=new Fixture();await f.Create();
        await f.Confirm(f.Session.ReviewDay(AdministrationOperation.PublishRaceDay));
        f.Session.SelectPlayer(QuoteTests.Horses[0]);await f.Session.ReadSelectedPlayerAsync(default);
        await f.Confirm(f.Session.ReviewEnrollment(QuoteTests.Horses[0],100,20,false));
        await f.Session.ReadSelectedPlayerAsync(default);
        await f.Confirm(f.Session.ReviewEnrollment(QuoteTests.Horses[0],7,3,true));
        await f.Session.ReadSelectedPlayerAsync(default);await f.Session.ReadPlayersAsync();
        Assert.Equal(130,f.Session.CurrentAccount!.Balance);Assert.Single(f.Session.EnrolledPlayers!.Players);
        await f.Confirm(f.Session.ReviewDay(AdministrationOperation.StartRaceDay));
        Assert.Throws<InvalidOperationException>(()=>f.Session.ReviewEnrollment(QuoteTests.Horses[0],1,0,true));
        await f.Confirm(f.Session.ReviewRace(AdministrationOperation.OpenBetting,QuoteTests.RaceId));
        await f.Confirm(f.Session.ReviewRace(AdministrationOperation.CloseBetting,QuoteTests.RaceId));
        Assert.Equal(7,f.Server.Calls);
        Assert.All(f.Session.AdministrationHistory(ConnectionTests.Day),h=>Assert.Equal("Acked",h.State));
    }
    [Fact] public async Task EventAdminDoesNotRequireCashierOperatingGrants() {
        using var f=new Fixture(["EVENT_ADMIN"]);await f.Create();
        Assert.True(f.Session.IsEventAdmin);Assert.Null(f.Session.CashierIdentifier);
        Assert.Throws<InvalidOperationException>(()=>f.Session.ReviewEnrollment(QuoteTests.Horses[0],1,0,false));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.ReadPlayersAsync());
    }
    [Theory] [InlineData("CASHIER")] [InlineData("AUDITOR")] [InlineData("PLAYER")]
    public async Task OtherRolesCannotCreateDay(string role) {
        using var f=new Fixture([role]);await f.Ready();
        Assert.Throws<InvalidOperationException>(()=>f.Session.ReviewCreate(new(Definition())));Assert.Equal(0,f.Server.Calls);
    }
    [Fact] public async Task PlayerGrantIsRequiredEvenForCombinedAdminAuditorCashier() {
        using var f=new Fixture(player:false);await f.Create();
        Assert.Throws<InvalidOperationException>(()=>f.Session.ReviewEnrollment(QuoteTests.Horses[0],1,0,false));
        Assert.Equal(1,f.Server.Calls);
    }
    [Fact] public async Task ChangedVersionAndActorInvalidateReviewWithoutDispatch() {
        using var f=new Fixture();await f.Create();
        var review=f.Session.ReviewDay(AdministrationOperation.PublishRaceDay);
        f.Server.Day!.Status.Version++;await f.Session.SynchronizeAsync(ConnectionTests.Day,default);
        Assert.False(f.Session.IsAdministrationReviewCurrent(review.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.ConfirmAdministrationAsync(review.Id));
        f.Session.CancelAdministrationReview();var next=f.Session.ReviewDay(AdministrationOperation.PublishRaceDay);
        f.Session.SignOut();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.ConfirmAdministrationAsync(next.Id));
        Assert.Equal(1,f.Server.Calls);
    }
    [Fact] public async Task InFlightAckIsSavedButCannotPopulateNewOperator() {
        using var f=new Fixture();await f.Create();
        f.Server.Delayed=new();var review=f.Session.ReviewDay(AdministrationOperation.PublishRaceDay);
        var send=f.Session.ConfirmAdministrationAsync(review.Id);
        f.Session.SignOut();f.Login.Subject="other";await f.Ready();
        f.Server.Delayed.SetResult(new DayAcknowledgement { RaceDayId=ConnectionTests.Day.ToString(),Phase=DayPhase.Ready,Version=1 }.ToByteArray());
        await send;
        Assert.Equal("",f.Session.AdministrationStatus);Assert.Empty(f.Session.AdministrationHistory(ConnectionTests.Day));
        var original=new AdministrationBinding("test","https://api.example.invalid","https://identity.example.invalid","desktop","actor",ConnectionTests.Day.ToString());
        Assert.Equal(MutationState.Acked,f.Journal.Read(original).Single(r=>r.Id==review.Id).State);
    }
    [Fact] public async Task ExplicitFirstRejectionPermitsNewHumanReviewButNeverAutomaticRetry() {
        using var f=new Fixture();await f.Create();f.Server.Reject=StatusCode.Aborted;
        await f.Session.ConfirmAdministrationAsync(f.Session.ReviewDay(AdministrationOperation.PublishRaceDay).Id);
        Assert.Equal(2,f.Server.Calls);Assert.Contains(f.Session.AdministrationHistory(ConnectionTests.Day),h=>h.State=="Rejected"&&!h.CanRecover);
        f.Server.Reject=null;
        await f.Confirm(f.Session.ReviewDay(AdministrationOperation.PublishRaceDay));Assert.Equal(3,f.Server.Calls);
    }
    [Fact] public async Task UnchangedAccountPreservesFundingReviewButChangedVersionInvalidatesIt()
    {
        using var f=new Fixture();await f.Create();
        f.Session.SelectPlayer(QuoteTests.Horses[0]);
        await f.Confirm(f.Session.ReviewEnrollment(QuoteTests.Horses[0],100,0,false));
        await f.Session.ReadSelectedPlayerAsync(default);
        var review=f.Session.ReviewEnrollment(QuoteTests.Horses[0],10,0,true);
        await f.Session.ReadSelectedPlayerAsync(default);
        Assert.True(f.Session.IsAdministrationReviewCurrent(review.Id));
        f.Server.Account!.AccountVersion++;
        await f.Session.ReadSelectedPlayerAsync(default);
        Assert.False(f.Session.IsAdministrationReviewCurrent(review.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.ConfirmAdministrationAsync(review.Id));
        Assert.Equal(2,f.Server.Calls);
    }
    [Fact] public async Task FundingOverflowCannotPrepareOrDispatch()
    {
        using var f=new Fixture();await f.Create();
        Assert.Throws<OverflowException>(()=>f.Session.ReviewEnrollment(QuoteTests.Horses[0],long.MaxValue,1,false));
        Assert.Equal(1,f.Server.Calls);
    }
    [Theory] [InlineData("0",true)] [InlineData("9223372036854775807",true)] [InlineData("9223372036854775808",false)]
    [InlineData("-1",false)] [InlineData("+1",false)] [InlineData("1.0",false)] [InlineData(" 1",false)] [InlineData("١",false)]
    public void ExactNonnegativeFunding(string text,bool valid)=>Assert.Equal(valid,ConnectionSession.TryFunding(text,out _));

    [Fact] public void EncryptedDraftScopeFreezeCorruptionAndPendingFailClosed() {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try {
            var binding=new AdministrationBinding("p","https://api.invalid","https://issuer.invalid","c","actor",ConnectionTests.Day.ToString());
            var store=new LocalPlanStore(directory);var plan=new LocalRaceDayPlan(Definition());
            store.Save(binding,plan);Assert.Equal(plan.Definition,store.Load(binding)!.Definition);
            Assert.Null(store.Load(binding with { Subject="other" }));
            plan.Freeze([ConnectionTests.Day],[ConnectionTests.Day.ToString()]);store.Save(binding,plan);
            Assert.Equal(plan.Serialize(),new LocalPlanStore(directory).Load(binding)!.Serialize());
            Assert.Throws<IOException>(()=>store.Discard(binding));
            Assert.Throws<IOException>(()=>store.Save(binding,new(Definition())));
            var path=System.IO.Directory.GetFiles(directory,"*.plan.dpapi").Single();
            File.WriteAllBytes(path+".pending",[1]);Assert.Throws<IOException>(()=>store.Load(binding));File.Delete(path+".pending");
            File.WriteAllBytes(path,[1,2,3]);Assert.ThrowsAny<Exception>(()=>store.Load(binding));
        } finally { System.IO.Directory.Delete(directory,true); }
    }
}
