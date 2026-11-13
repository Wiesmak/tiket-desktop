using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;
namespace Tiket.Desktop.Tests;
public class Phase7Tests {
    [Fact] public void ExactAmountsAndReceiptLabels() {
        Assert.Contains("9223372036854775807",AccountPresentation.Account(new() { Balance=long.MaxValue }));
        Assert.Contains("9223372036854775807",AccountPresentation.Quote(new() { Account=new(),Withdrawable=long.MaxValue }));
        Assert.Contains("Recorded cashout",AccountPresentation.Receipt(new() { Withdrawn=9 }));
    }
    [Fact] public async Task SequencePaginationUsesLastSequenceWithoutDroppingRows() {
        var calls=new List<long>();
        var rows=await AccountPresentation.Movements(after=>{
            calls.Add(after);
            return Task.FromResult(after==0 ? new AccountMovements { Movements={new FinancialMovement { Sequence=7 },new FinancialMovement { Sequence=11 }}, NextSequence=11 } :
                new AccountMovements { Movements={new FinancialMovement { Sequence=14 }} });
        });
        Assert.Equal(new long[]{0,11},calls); Assert.Equal(new long[]{7,11,14},rows.Select(r=>r.Sequence));
        await Assert.ThrowsAsync<IOException>(()=>AccountPresentation.Movements(_=>Task.FromResult(new AccountMovements { NextSequence=0 })));
        await Assert.ThrowsAsync<IOException>(()=>AccountPresentation.Movements(_=>Task.FromResult(new AccountMovements { Movements={new FinancialMovement { Sequence=1 },new FinancialMovement { Sequence=1 }} })));
    }
    [Theory] [InlineData("CashOut")] [InlineData("FinalizeRaceDay")] [InlineData("CompleteRaceDay")] [InlineData("CloseRaceDay")]
    public async Task GeneratedMutationTransportBindsExactRequestAndBearer(string method) {
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start();
        var profile=ConnectionTests.Profile(fixture.Address,development:true);using var backend=new ReadOnlyBackend(profile);
        var r=Record();
        if(method!="CashOut") r=r with {Method=AdministrationWire.Prefix+"RaceDayService/"+method,Request=new DayCommand {RaceDayId=r.Binding.Event,ExpectedVersion=4,IdempotencyKey=r.Key}.ToByteArray()};
        r=r with {State=MutationState.Sending,Binding=new(profile.Id,profile.Backend.OriginalString,profile.Issuer.OriginalString,profile.ClientId,"actor",r.Binding.Event)};
        var bytes=await ((IAdministrationBackend)backend).TransmitAdministrationAsync(r,"access-only",default);
        Assert.Equal("/"+r.Method,fixture.AdministrationPath);Assert.Equal(r.Request,fixture.AdministrationRequest);
        Assert.Equal(new[]{"Bearer access-only"},fixture.Calls.Single().Auth);Assert.NotEmpty(fixture.Calls.Single().Timeout);
        AdministrationWire.Verify(r,bytes);
    }
    static AdministrationRecord Record() {
        var r=new CashOutRequest { RaceDayId=ConnectionTests.Day.ToString(),PlayerId=QuoteTests.Horses[0],ExpectedAccountVersion=8,ExpectedDayVersion=4,IdempotencyKey=Guid.NewGuid().ToString() };
        return new(1,Guid.NewGuid(),new("test","http://backend","https://issuer","client","actor",r.RaceDayId),AdministrationWire.Prefix+"AccountService/CashOut",r.ToByteArray(),r.IdempotencyKey,DateTimeOffset.UtcNow,MutationState.Prepared,null);
    }
    static byte[] Receipt(AdministrationRecord r) => new CashoutReceipt { RaceDayId=r.Binding.Event,PlayerId=CashOutRequest.Parser.ParseFrom(r.Request).PlayerId,AccountVersion=9,Withdrawn=10,Currency="PLN",CashedOutAt="2026-11-13T00:00:00Z",ReceiptReference="receipt" }.ToByteArray();
    [Fact] public async Task UnknownCrashRecoveryPreservesRequestAndAckCannotResend() {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); var r=Record();
        try {
            using(var journal=new AdministrationJournal(dir)) { journal.Prepare(r); journal.MarkSending(r.Id,r.Binding); }
            using(var journal=new AdministrationJournal(dir)) {
                Assert.Equal(MutationState.Unknown,journal.Read(r.Binding).Single().State);
                Assert.Empty(journal.Read(r.Binding with { Subject="other" })); var calls=0;
                var ack=await new JournaledAdministration(journal).SendAsync(r.Id,r.Binding,(saved,_)=>{calls++;Assert.Equal(r.Request,saved.Request);Assert.Equal(r.Key,saved.Key);return Task.FromResult(Receipt(r));},default);
                Assert.Equal(Receipt(r),ack.Response);
                await Assert.ThrowsAnyAsync<Exception>(()=>new JournaledAdministration(journal).SendAsync(r.Id,r.Binding,(_,_)=>{calls++;return Task.FromResult(Receipt(r));},default));
                Assert.Equal(1,calls);
                var bad=CashoutReceipt.Parser.ParseFrom(Receipt(r));bad.PlayerId=Guid.NewGuid().ToString();
                Assert.Throws<IOException>(()=>AdministrationWire.Verify(r,bad.ToByteArray()));
            }
        } finally {Directory.Delete(dir,true);}
    }
    [Theory] [InlineData("FinalizeRaceDay",DayPhase.Finalizing)] [InlineData("CompleteRaceDay",DayPhase.Completed)] [InlineData("CloseRaceDay",DayPhase.Closed)]
    public void DayAcknowledgementPhaseAndVersion(string method,DayPhase phase) {
        var r=Record();var c=new DayCommand { RaceDayId=r.Binding.Event,ExpectedVersion=4,IdempotencyKey=r.Key };
        r=r with { Method=AdministrationWire.Prefix+"RaceDayService/"+method,Request=c.ToByteArray() };
        Assert.Equal((c.RaceDayId,c.IdempotencyKey),AdministrationWire.Identity(r.Method,r.Request));
        var ack=new DayAcknowledgement { RaceDayId=c.RaceDayId,Phase=phase,Version=5 };
        AdministrationWire.Verify(r,ack.ToByteArray());ack.Version=4;
        Assert.Throws<IOException>(()=>AdministrationWire.Verify(r,ack.ToByteArray()));
    }
    sealed class Login(string[] roles,bool player,bool grant=true) : INativeLogin {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p,Action<ConnectionState> progress,CancellationToken ct) =>
            Task.FromResult(new LoginMaterial("actor","synthetic",DateTimeOffset.UtcNow.AddMinutes(5)) { AdministrationGrants=new("actor",roles,grant?[ConnectionTests.Day.ToString()]:[],player?[QuoteTests.Horses[0]]:[]) });
    }
    sealed class Server : IReadOnlyBackend,IAdministrationBackend,IAccountBackend,IAccountHistoryBackend {
        public DayPhase Phase=DayPhase.Completed; public long Version=4; public bool Cashed; public int Quotes,Calls;
        public TaskCompletionSource<AccountMovements>? Pending;
        public Task<PingResponse> PingAsync(CancellationToken ct)=>Task.FromResult(new PingResponse());
        public Task<EventOverview> ReadAsync(Guid d,string t,CancellationToken ct)=>Task.FromResult(new EventOverview(new() {Definition=AdministrationSessionTests.Definition(),Status=new() { RaceDayId=d.ToString(),Phase=Phase,Version=Version }},new(),DateTimeOffset.UtcNow));
        AccountInformation Account(string day,string player)=>new() { RaceDayId=day,PlayerId=player,Phase=Phase,AccountVersion=8,Balance=Cashed?0:10,Currency="PLN",CashedOut=Cashed };
        public Task<AccountInformation> GetAccountAsync(AccountQuery q,string t,CancellationToken ct)=>Task.FromResult(Account(q.RaceDayId,q.PlayerId));
        public Task<EnrollmentInformation> GetEnrollmentAsync(EnrollmentQuery q,string t,CancellationToken ct)=>Task.FromResult(new EnrollmentInformation {RaceDayId=q.RaceDayId,PlayerId=q.PlayerId});
        public Task<PlayerList> ListPlayersAsync(DayQuery q,string t,CancellationToken ct)=>Task.FromResult(new PlayerList());
        public Task<byte[]> TransmitAdministrationAsync(AdministrationRecord r,string t,CancellationToken ct) {Calls++;return Task.FromResult(Receipt(r));}
        public Task<WithdrawalQuoteInformation> QuoteAsync(AccountQuery q,string t,CancellationToken ct) { Quotes++;return Task.FromResult(new WithdrawalQuoteInformation {Account=Account(q.RaceDayId,q.PlayerId),RaceDayVersion=Version,Withdrawable=10});}
        public Task<CashoutReceipt> ReceiptAsync(AccountQuery q,string t,CancellationToken ct)=>Task.FromResult(new CashoutReceipt {RaceDayId=q.RaceDayId,PlayerId=q.PlayerId,Balance=0,ReceiptReference="receipt"});
        public Task<AccountMovements> MovementsAsync(AccountMovementsRequest q,string t,CancellationToken ct) {Assert.Equal(100,q.PageSize);return Pending?.Task??Task.FromResult(new AccountMovements());}
        public void Dispose(){}
    }
    [Theory] [InlineData("CASHIER",true,true)] [InlineData("CASHIER",false,false)] [InlineData("EVENT_ADMIN",true,false)] [InlineData("AUDITOR",true,false)]
    public async Task CashoutRequiresCashierPlayerGrantAndBindsVersions(string role,bool player,bool allowed) {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try {
            using var journal=new AdministrationJournal(dir);var server=new Server();
            using var session=new ConnectionSession(new Login([role],player),_=>server);
            session.UseAdministrationStorage(journal,Path.Combine(dir,"drafts"));session.Select(ConnectionTests.Profile());await session.SignInAsync();await session.SynchronizeAsync(ConnectionTests.Day,default);
            session.SelectPlayer(QuoteTests.Horses[0]);await session.ReadSelectedPlayerAsync(default);await session.ReadAccountHistoryAsync(default);
            if(!allowed) {Assert.Throws<InvalidOperationException>(()=>session.ReviewCashOut());Assert.Equal(0,server.Calls);return;}
            var review=session.ReviewCashOut();Assert.Contains("10 PLN",review.Payload);
            server.Version++;await session.SynchronizeAsync(ConnectionTests.Day,default);
            Assert.False(session.IsAdministrationReviewCurrent(review.Id));await Assert.ThrowsAsync<InvalidOperationException>(()=>session.ConfirmAdministrationAsync(review.Id));Assert.Equal(0,server.Calls);
        } finally {Directory.Delete(dir,true);}
    }
    [Theory] [InlineData("EVENT_ADMIN",true,DayPhase.Running,AdministrationOperation.FinalizeRaceDay,true)]
    [InlineData("CASHIER",true,DayPhase.Running,AdministrationOperation.FinalizeRaceDay,false)]
    [InlineData("EVENT_ADMIN",false,DayPhase.Running,AdministrationOperation.FinalizeRaceDay,false)]
    [InlineData("EVENT_ADMIN",true,DayPhase.Finalizing,AdministrationOperation.CompleteRaceDay,true)]
    [InlineData("EVENT_ADMIN",true,DayPhase.Completed,AdministrationOperation.CloseRaceDay,true)]
    [InlineData("EVENT_ADMIN",true,DayPhase.Running,AdministrationOperation.CloseRaceDay,false)]
    public async Task DayTransitionsRequireIndependentRoleEventAndPhase(string role,bool grant,DayPhase phase,AdministrationOperation operation,bool allowed) {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try {
            using var journal=new AdministrationJournal(dir);var server=new Server {Phase=phase};
            using var session=new ConnectionSession(new Login([role],true,grant),_=>server);
            session.UseAdministrationStorage(journal,Path.Combine(dir,"drafts"));session.Select(ConnectionTests.Profile());await session.SignInAsync();await session.SynchronizeAsync(ConnectionTests.Day,default);
            if(allowed) Assert.True(session.IsAdministrationReviewCurrent(session.ReviewDay(operation).Id));
            else Assert.Throws<InvalidOperationException>(()=>session.ReviewDay(operation));
            Assert.Equal(0,server.Calls);
        } finally {Directory.Delete(dir,true);}
    }
    [Fact] public async Task QuoteOnlyCompletedReceiptAfterCashoutAndStaleSwitchDiscards() {
        var server=new Server {Phase=DayPhase.Running};
        using var session=new ConnectionSession(new Login(["CASHIER"],true),_=>server);
        session.Select(ConnectionTests.Profile());await session.SignInAsync();await session.SynchronizeAsync(ConnectionTests.Day,default);
        session.SelectPlayer(QuoteTests.Horses[0]);await session.ReadSelectedPlayerAsync(default);await session.ReadAccountHistoryAsync(default);Assert.Equal(0,server.Quotes);
        server.Phase=DayPhase.Completed;server.Cashed=true;await session.SynchronizeAsync(ConnectionTests.Day,default);await session.ReadSelectedPlayerAsync(default);await session.ReadAccountHistoryAsync(default);
        Assert.Equal(0,server.Quotes);Assert.Contains("receipt",session.WithdrawalText);Assert.False(session.CanCashOut);
        server.Pending=new();var read=session.ReadAccountHistoryAsync(default);session.SelectPlayer(QuoteTests.Horses[1]);server.Pending.SetResult(new());Assert.False(await read);Assert.Empty(session.WithdrawalText);
    }
}