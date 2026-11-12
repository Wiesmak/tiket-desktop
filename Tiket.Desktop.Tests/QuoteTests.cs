using System.Collections.Immutable;
using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class QuoteTests
{
    internal static readonly string[] Horses = Enumerable.Range(1, 4).Select(i => $"00000000-0000-0000-0000-{i:000000000000}").ToArray();
    internal const string RaceId = "ad238129-b219-4a6e-908f-a5da6c73e932";
    internal static RaceDayInformation Day() => new() { Definition = new() { RaceDayId = ConnectionTests.Day.ToString() }, Status = new() { Phase = DayPhase.Running, Version = 7 } };
    internal static RaceInformation Race() => new() { Definition = new() { RaceId = RaceId,
        Runners = { Horses.Select((id, i) => new Runner { HorseId = id, ProgramNumber = $"0{i+1}", DisplayName = $"Runner {i+1}" }) },
        Pools = { Enum.GetValues<PoolType>().Where(p => CashierComposer.Arity(p) > 0).Select(p => new PoolConfiguration { Type = p }) } },
        Status = new() { Phase = RacePhase.BettingOpen, Version = 13 } };
    internal static CashierComposer Editor()
    {
        var c = new CashierComposer(); c.SetContext(Day(), Race());
        c.Edit(QuoteDraft.Empty with { Slots = [new(Horses[0])] }); return c;
    }
    // Deliberately varying fixture terms: these are server output, never calculated by the client.
    internal static QuoteCouponResponse Reply() => new() {
        Pricing = new() { BetType = PoolType.Quinella, Selection = new() { Positional = new() { Positions = { new Position { HorseId = Horses[1] }, new Position { HorseId = Horses[0] } } } },
            CombinationCount = 3, UnitsPerCombination = 17, TotalUnits = 51, UnitStake = 23, TotalStake = 1173, Currency = "PLN" },
        RaceDayVersion = 77, RaceVersion = 81, Confirmation = "opaque-server-confirmation" };

    [Theory]
    [InlineData("1", true)] [InlineData("9223372036854775807", true)]
    [InlineData("9223372036854775808", false)] [InlineData("9007199254740993", true)]
    [InlineData("", false)] [InlineData("0", false)] [InlineData("-1", false)]
    [InlineData("+1", false)] [InlineData("1.0", false)] [InlineData(" 1", false)]
    [InlineData("١", false)] [InlineData("11111111111111111111", false)]
    public void ExactUnits(string text, bool valid) => Assert.Equal(valid, CashierComposer.TryUnits(text, out _));

    [Theory]
    [InlineData(PoolType.Win, SelectionMode.Normal)] [InlineData(PoolType.Win, SelectionMode.Box)]
    [InlineData(PoolType.Quinella, SelectionMode.Normal)] [InlineData(PoolType.Quinella, SelectionMode.Wall)] [InlineData(PoolType.Quinella, SelectionMode.Box)]
    [InlineData(PoolType.Exacta, SelectionMode.Normal)] [InlineData(PoolType.Exacta, SelectionMode.Wall)] [InlineData(PoolType.Exacta, SelectionMode.Box)]
    [InlineData(PoolType.Trifecta, SelectionMode.Normal)] [InlineData(PoolType.Trifecta, SelectionMode.Wall)] [InlineData(PoolType.Trifecta, SelectionMode.Box)]
    public void AllSupportedShapesRoundTripExactWire(PoolType pool, SelectionMode mode)
    {
        using var c = Editor();
        int arity = CashierComposer.Arity(pool);
        var slots = Enumerable.Range(0, arity).Select(i => mode == SelectionMode.Wall && i == arity-1 ? new SlotChoice(IsWall:true) : new SlotChoice(Horses[arity-i-1])).ToImmutableArray();
        c.Edit(new(pool, mode, slots, Horses.Take(Math.Max(2, arity)).ToImmutableArray(), "9223372036854775807"));
        Assert.Null(c.Validation());
        var actual = QuoteCouponRequest.Parser.ParseFrom(c.Request().ToByteArray());
        Assert.Equal(long.MaxValue, actual.UnitsPerCombination);
        Assert.Equal(ConnectionTests.Day.ToString(), actual.RaceDayId);
        Assert.Equal(RaceId, actual.RaceId); Assert.Equal(pool, actual.BetType);
        if (mode == SelectionMode.Box) { Assert.Equal(BettingSelection.VariantOneofCase.Box, actual.Selection.VariantCase); Assert.Equal(c.Draft.Box, actual.Selection.Box.HorseIds); }
        else {
            Assert.Equal(BettingSelection.VariantOneofCase.Positional, actual.Selection.VariantCase);
            Assert.Equal(arity, actual.Selection.Positional.Positions.Count);
            for(int i=0;i<arity;i++) {
                if(slots[i].IsWall) Assert.True(actual.Selection.Positional.Positions[i].Wall);
                else Assert.Equal(slots[i].HorseId, actual.Selection.Positional.Positions[i].HorseId);
            }
        }
    }
    [Fact] public void InvalidSelectionsFailClosed()
    {
        using var c = Editor();
        void Invalid(QuoteDraft d) { c.Edit(d); Assert.NotNull(c.Validation()); Assert.Throws<InvalidOperationException>(() => c.Request()); }
        Invalid(new(PoolType.Win, SelectionMode.Wall, [new(IsWall:true)], [], "1"));
        Invalid(new(PoolType.Win, SelectionMode.Box, [], [Horses[0]], "1"));
        Invalid(new(PoolType.Trifecta, SelectionMode.Box, [], [Horses[0], Horses[1]], "1"));
        Invalid(new(PoolType.Exacta, SelectionMode.Normal, [new(Horses[0]),new(Horses[0])], [], "1"));
        Invalid(new(PoolType.Exacta, SelectionMode.Wall, [new(IsWall:true),new(IsWall:true)], [], "1"));
        Invalid(new(PoolType.Win, SelectionMode.Normal, [new(Guid.NewGuid().ToString())], [], "1"));
        Invalid(new(PoolType.Exacta, SelectionMode.Normal, [new(Horses[0])], [], "1"));
        Invalid(new(PoolType.Win, SelectionMode.Box, [], [Horses[0],Horses[0]], "1"));
        c.Edit(QuoteDraft.Empty with { Slots = [new(Horses[0])] });
        var r = Race(); r.Status.ScratchedHorseIds.Add(Horses[0]); c.SetContext(Day(),r);
        Assert.Contains("scratched", c.Validation()); Assert.Equal(Horses[0],c.Draft.Slots[0].HorseId);
    }
    [Fact] public async Task AuthoritativeResponseIsRetainedWithoutRecalculationOrAliasing()
    {
        using var c = Editor(); var reply = Reply(); var bytes = reply.ToByteArray();
        await c.QuoteAsync((_,_) => Task.FromResult(reply));
        Assert.Equal(QuoteState.Quoted,c.State); Assert.Equal(bytes,c.Response!.ToByteArray()); Assert.NotNull(c.QuotedAt);
        reply.Pricing.TotalStake = 1; c.Response!.Pricing.TotalStake = 2;
        Assert.Equal(1173,c.Response!.Pricing.TotalStake);
        Assert.Equal("02 · Runner 2 / 01 · Runner 1",CashierComposer.Canonical(c.Response.Pricing.Selection,Race().Definition.Runners));
        Assert.Contains("01",CashierComposer.RunnerLabel(Race().Definition.Runners[0]));
    }
    [Theory]
    [InlineData("units")] [InlineData("pool")] [InlineData("mode")] [InlineData("horse")]
    [InlineData("race")] [InlineData("day")] [InlineData("scratch")] [InlineData("refresh")]
    [InlineData("cancel")] [InlineData("shutdown")] [InlineData("rapid")]
    public async Task EveryChangeSuppressesLateResponse(string dimension)
    {
        using var c = Editor(); var completion = new TaskCompletionSource<QuoteCouponResponse>();
        CancellationToken observed = default;
        var pending = c.QuoteAsync((_,ct) => { observed = ct; return completion.Task; });
        var d = c.Draft;
        switch(dimension) {
            case "units": c.Edit(d with { Units="2" }); break;
            case "pool": c.Edit(d with { Pool=PoolType.Exacta }); break;
            case "mode": c.Edit(d with { Mode=SelectionMode.Box }); break;
            case "horse": c.Edit(d with { Slots=[new(Horses[1])] }); break;
            case "cancel": c.Cancel(); break;
            case "shutdown": c.Dispose(); break;
            case "rapid": for(int i=0;i<30;i++) c.Edit(d with { Units=(i+1).ToString() }); break;
            default: var r=Race(); var day=Day();
                if(dimension=="race") r.Definition.RaceId=Guid.NewGuid().ToString();
                if(dimension=="day") day.Definition.RaceDayId=Guid.NewGuid().ToString();
                if(dimension=="scratch") r.Status.ScratchedHorseIds.Add(Horses[0]);
                c.SetContext(day,r); break;
        }
        Assert.True(observed.IsCancellationRequested);
        completion.SetResult(Reply()); await pending;
        Assert.Null(c.Response); Assert.NotEqual(QuoteState.Quoted,c.State);
    }
    [Theory]
    [InlineData(StatusCode.Unauthenticated,QuoteState.Expired)] [InlineData(StatusCode.PermissionDenied,QuoteState.Denied)]
    [InlineData(StatusCode.InvalidArgument,QuoteState.Invalid)] [InlineData(StatusCode.FailedPrecondition,QuoteState.Stale)]
    [InlineData(StatusCode.ResourceExhausted,QuoteState.Limited)] [InlineData(StatusCode.Unavailable,QuoteState.Offline)]
    [InlineData(StatusCode.DeadlineExceeded,QuoteState.Offline)] [InlineData(StatusCode.Cancelled,QuoteState.Cancelled)]
    [InlineData(StatusCode.NotFound,QuoteState.UnknownEvent)] [InlineData(StatusCode.Internal,QuoteState.Error)]
    public async Task SafeDistinctErrors(StatusCode status, QuoteState state)
    {
        using var c=Editor(); await c.QuoteAsync((_,_)=>Task.FromException<QuoteCouponResponse>(new RpcException(new Status(status,"SECRET"))));
        Assert.Equal(state,c.State); Assert.DoesNotContain("SECRET",c.Message); Assert.Null(c.Response);
    }
    [Theory] [InlineData("signout")] [InlineData("profile")] [InlineData("expiry")] [InlineData("refresh")] [InlineData("shutdown")]
    public async Task SessionChangesSuppressLateQuote(string change)
    {
        var login = new Login(); var backend=new Backend();
        using var session=new ConnectionSession(login,_=>backend);
        session.Select(ConnectionTests.Profile()); await session.SignInAsync(); await session.ReadAsync(ConnectionTests.Day);
        session.Composer.SetContext(Day(),Race()); session.Composer.Edit(QuoteDraft.Empty with { Slots=[new(Horses[0])] });
        var pending=session.QuoteAsync();
        switch(change) {
            case "signout": session.SignOut(); break;
            case "profile": session.Select(ConnectionTests.Profile("https://other.example.invalid")); break;
            case "expiry": await Task.Delay(220); session.CheckExpiry(); break;
            case "refresh": await session.ReadAsync(ConnectionTests.Day); break;
            case "shutdown": session.Dispose(); break;
        }
        backend.Completion.SetResult(Reply()); await pending;
        Assert.Null(session.Composer.Response); Assert.True(backend.Token.IsCancellationRequested);
    }
    [Theory] [InlineData(2)] [InlineData(12)] [InlineData(32)]
    public async Task DifferentRosterSizesAndCompletedQuoteInvalidation(int size)
    {
        using var c=Editor(); var race=Race();
        race.Definition.Runners.Clear();
        race.Definition.Runners.Add(Enumerable.Range(1,size).Select(i=>new Runner {
            HorseId=$"00000000-0000-0000-0000-{i:000000000000}",ProgramNumber=$"{i:00}",DisplayName=$"Runner {i}" }));
        c.SetContext(Day(),race);
        c.Edit(c.Draft with { Mode=SelectionMode.Box,Box=[Horses[0],Horses[1]] });
        Assert.Null(c.Validation());
        await c.QuoteAsync((_,_)=>Task.FromResult(Reply()));
        Assert.NotNull(c.Response);
        c.Edit(c.Draft with { Units="2" });
        Assert.Null(c.Response); Assert.Null(c.QuotedAt); Assert.Equal(QuoteState.Editing,c.State);
        await c.QuoteAsync((_,_)=>Task.FromResult(Reply()));
        race.Status.Version++; race.Status.ScratchedHorseIds.Add(Horses[0]);
        c.SetContext(Day(),race);
        Assert.Null(c.Response); Assert.Contains(Horses[0],c.Draft.Box);
        Assert.Contains("scratched",c.Validation());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ClosedDayOrRaceDisablesQuotation(bool closeDay)
    {
        using var c=Editor(); var d=Day(); var r=Race();
        if(closeDay) d.Status.Phase=DayPhase.Ready; else r.Status.Phase=RacePhase.BettingClosed;
        c.SetContext(d,r); Assert.NotNull(c.Validation());
        Assert.Throws<InvalidOperationException>(()=>c.Request());
    }
    private sealed class Login : INativeLogin {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p,Action<ConnectionState> progress,CancellationToken ct)=>Task.FromResult(new LoginMaterial("fixture","access",DateTimeOffset.UtcNow.AddMilliseconds(180)));
    }
    private sealed class Backend : IReadOnlyBackend {
        public TaskCompletionSource<QuoteCouponResponse> Completion=new(); public CancellationToken Token;
        public Task<PingResponse> PingAsync(CancellationToken ct)=>Task.FromResult(new PingResponse());
        public Task<EventOverview> ReadAsync(Guid day,string accessToken,CancellationToken ct)=>Task.FromResult(new EventOverview(Day(),new RaceList { Races={Race()} },DateTimeOffset.UtcNow));
        public Task<QuoteCouponResponse> QuoteAsync(QuoteCouponRequest r,string accessToken,CancellationToken ct) { Token=ct;return Completion.Task; }
        public void Dispose() {}
    }
}
