using Tiket.Desktop.Application;
using Pl.Umamusume.Tiket.Api.V1;
using System.Text.Json;
using Xunit;

namespace Tiket.Desktop.Tests;

public sealed class StaffPreparationTests
{
    private sealed class ProvisioningLogin(Guid added, bool admin) : INativeLogin
    {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile profile, Action<ConnectionState> progress, CancellationToken cancellation) =>
            Task.FromResult(new LoginMaterial("operator", "test-token", DateTimeOffset.UtcNow.AddMinutes(5)) {
                AdministrationGrants = new("operator", admin ? ["EVENT_ADMIN"] : ["CASHIER"], [added.ToString("D")], [])
            });
    }
    [Theory] [InlineData(true, true, true)] [InlineData(true, false, false)] [InlineData(false, true, false)]
    public async Task ProvisioningCapabilityRequiresExplicitLabProfileAndAdmin(bool development, bool admin, bool expected)
    {
        var added = Guid.NewGuid();
        var profile = new ConnectionProfile("lab", "Lab", "https://127.0.0.1:9090", "https://localhost:8443/realms/tiket-lab", "tiket-desktop", "tiket-api", ["openid"], [], development);
        using var session = new ConnectionSession(new ProvisioningLogin(added, admin));
        session.Select(profile); await session.SignInAsync();
        Assert.Equal(expected, session.CanProvisionLab);
        Assert.Equal(development, session.Profile!.KnownEvents.Contains(added));
        Assert.Equal(development, session.ProvisionedEvents.Contains(added));
        session.SignOut(); Assert.False(session.CanProvisionLab);
    }
    [Fact] public void NormalComparisonHonorsUnorderedQuinellaAndOrderedExacta()
    {
        BettingSelection S(params string[] horses) => new() { Positional = new() { Positions = { horses.Select(h => new Position { HorseId = h }) } } };
        Assert.True(StaffPreparation.SameNormalSelection(PoolType.Quinella, S("a", "b"), S("b", "a")));
        Assert.False(StaffPreparation.SameNormalSelection(PoolType.Exacta, S("a", "b"), S("b", "a")));
        Assert.False(StaffPreparation.SameNormalSelection(PoolType.Quinella, S("a", "b"), S("a", "c")));
    }
    [Fact] public void RoundSerializationRetainsEveryPurchaseIdentityAndExactQuoteBytes()
    {
        var binding = new MutationBinding("p", "https://api.invalid", "https://issuer.invalid", "client", "actor", Guid.NewGuid().ToString(), 1);
        var original = new RaceRound(binding, Guid.NewGuid().ToString(), [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid(), "Player 01"), 3, 2, [1, 2, 3])]);
        var resumed = JsonSerializer.Deserialize<RaceRound>(JsonSerializer.SerializeToUtf8Bytes(original))!;
        Assert.Equal(binding, resumed.Binding); Assert.Equal(original.Race, resumed.Race);
        Assert.Equal(original.Entries[0].Id, resumed.Entries[0].Id); Assert.Equal(original.Entries[0].Coupon, resumed.Entries[0].Coupon);
        Assert.Equal(original.Entries[0].Key, resumed.Entries[0].Key); Assert.Equal(original.Entries[0].Quote, resumed.Entries[0].Quote);
        Assert.Equal(2, resumed.Entries[0].Stake);
    }
    [Fact] public void ReportRequiresServerAccountsToReconcileAndExportsDisplayedRows()
    {
        var day = Guid.NewGuid().ToString(); var raceId = Guid.NewGuid().ToString(); var player = Guid.NewGuid().ToString();
        var race = new RaceInformation { Definition = new() { RaceId = raceId, Number = 1 }, Status = new() { RaceDayId = day, RaceId = raceId, Phase = RacePhase.Settled } };
        var overview = new EventOverview(new() { Definition = new() { RaceDayId = day, Currency = "PLN" }, Status = new() { Phase = DayPhase.Completed } }, new() { Races = { race } }, DateTimeOffset.Now);
        var settlement = new SettlementInformation { Pools = { new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }.Select(type => new PoolSettlementSummary { Type = type, ValidStake = type == PoolType.Win ? 10 : 0, Distributed = type == PoolType.Win ? 9 : 0 }) } };
        var winners = new WinnersReport([new("coupon", player, PoolType.Win, 1, "horse", 10, 10, 9, "PLN")], settlement, true, "Complete");
        var account = new AccountInformation { RaceDayId = day, PlayerId = player, Currency = "PLN", OpeningPromotional = 200, AcceptedStakes = 10, CreditedWinnings = 9, Balance = 199 };
        var races = new[] { new EventRacePayout(race, new(), winners) };
        var report = EventPayoutReport.Build(overview, races, [new("=Alias,\"quoted\"", account, null, null)]);
        Assert.True(report.Complete); Assert.Contains("Current balance: 199", report.Display());
        Assert.Contains("\"'=Alias,\"\"quoted\"\"\"", report.Csv());
        Assert.All(report.Rows(), row => Assert.Equal(report.Rows()[0].Length, row.Length));
        account.Balance++;
        Assert.False(EventPayoutReport.Build(overview, races, [new("Player", account, null, null)]).Complete);
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 2)]
    [InlineData(200, 100)] [InlineData(long.MaxValue, 4611686018427387904L)]
    public void HalfBalanceRoundsUpWithoutOverflow(long balance, long expected) =>
        Assert.Equal(expected, StaffPreparation.HalfBalanceStake(balance));

    [Fact] public void NegativeBalanceCannotBePlanned() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffPreparation.HalfBalanceStake(-1));

    [Fact] public void TemplateHasIndependentOrderedRostersAndSurvivesFrozenResume()
    {
        var day = Guid.NewGuid(); var definition = StaffPreparation.SixRaceTemplate(day, new(2026, 11, 13));
        Assert.Equal(new[] { 6, 6, 6, 12, 12, 12 }, definition.Races.Select(r => r.Runners.Count));
        Assert.Equal(54, definition.Races.SelectMany(r => r.Runners).Select(h => h.HorseId).Distinct().Count());
        Assert.All(definition.Races, r => {
            Assert.Equal(Enumerable.Range(1, r.Runners.Count).Select(n => n.ToString("00")), r.Runners.Select(h => h.ProgramNumber));
            Assert.All(r.Pools, p => { Assert.Equal(1, p.UnitStake); Assert.Equal(1000, p.TakeBasisPoints); });
        });
        var plan = new LocalRaceDayPlan(definition);
        Assert.Empty(plan.Validate([day], [day.ToString()]));
        var request = plan.Freeze([day], [day.ToString()]);
        Assert.Equal(request, LocalRaceDayPlan.Deserialize(plan.Serialize()).Freeze([day], [day.ToString()]));
        Assert.NotEqual(definition.Races[0].RaceId, StaffPreparation.SixRaceTemplate(day, new(2026, 11, 13)).Races[0].RaceId);
    }

    [Fact] public void AliasPreparationResumesIdentitiesAndIsolatesProfileAndEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tiket-alias-test-" + Guid.NewGuid());
        try
        {
            var day = Guid.NewGuid(); var store = new PlayerAliasStore(directory);
            var intent = store.PendingEvent("lab");
            Assert.Equal(intent, new PlayerAliasStore(directory).PendingEvent("lab"));
            Assert.Equal(intent, store.PendingEvent("lab", Guid.NewGuid()));
            Assert.NotEqual(intent, store.PendingEvent("lab", intent));
            var prepared = store.PrepareGroup("lab", day);
            Assert.Equal(24, prepared.Select(p => p.Player).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 24).Select(i => $"Player {i:00}"), prepared.Select(p => p.DisplayName));
            Assert.Equal(prepared, new PlayerAliasStore(directory).PrepareGroup("lab", day));
            var renamed = prepared.Select((p, i) => i == 0 ? p with { DisplayName = "Renamed" } : p).ToArray();
            store.Save("lab", day, renamed);
            Assert.Equal(prepared[0].Player, store.Load("lab", day)[0].Player);
            Assert.Equal("Renamed", store.Load("lab", day)[0].DisplayName);
            Assert.Empty(store.Load("other", day)); Assert.Empty(store.Load("lab", Guid.NewGuid()));
            Assert.Throws<IOException>(() => store.Save("lab", day, [prepared[0], prepared[0]]));
            Assert.Equal(renamed, store.Load("lab", day));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
