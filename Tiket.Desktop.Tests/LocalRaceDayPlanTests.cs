using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class LocalRaceDayPlanTests
{
    private static readonly Guid Day = Guid.NewGuid();
    private static RaceDayConfiguration Definition(int runners = 3) => new()
    {
        RaceDayId = Day.ToString(), EventDate = "2028-02-29", DisplayNumber = 1, Currency = "PLN",
        TimeZone = "Europe/Warsaw", RulesVersion = "1", MaximumCouponLegs = 100, MaximumOpenRaces = 1,
        NoWinnerPolicy = NoWinnerPolicy.HouseRetainsPool,
        Races = { new RaceConfiguration
        {
            RaceId = Guid.NewGuid().ToString(), Number = 1, Title = "Épreuve / 日本", RacecourseName = "Course",
            Runners = { Enumerable.Range(1, runners).Select(i => new Runner
            { HorseId = Guid.NewGuid().ToString(), ProgramNumber = i.ToString("00"), DisplayName = $"Horse {i}" }) },
            Pools = { new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }
                .Select(p => new PoolConfiguration { Type = p, UnitStake = long.MaxValue, TakeBasisPoints = 9999 }) }
        } }
    };
    [Theory] [InlineData(1)] [InlineData(3)] [InlineData(12)] [InlineData(32)]
    public void VariedRostersPreserveExactPayloadAndStableKey(int runners)
    {
        var definition = Definition(runners); var plan = new LocalRaceDayPlan(definition);
        Assert.Empty(plan.Validate([Day], [Day.ToString()]));
        var frozen = plan.Freeze([Day], [Day.ToString()]);
        Assert.Equal(definition, frozen.Definition);
        Assert.Equal("01", frozen.Definition.Races[0].Runners[0].ProgramNumber);
        Assert.Throws<InvalidOperationException>(() => plan.Replace(definition));
        var resumed = LocalRaceDayPlan.Deserialize(plan.Serialize());
        Assert.True(resumed.IsFrozen); Assert.Equal(frozen, resumed.Freeze([Day], [Day.ToString()]));
        frozen.Definition.Currency = "USD";
        Assert.Equal("PLN", plan.Definition.Currency);
    }
    [Theory]
    [InlineData("event")] [InlineData("date")] [InlineData("zone")] [InlineData("currency")]
    [InlineData("race")] [InlineData("number")] [InlineData("horse")] [InlineData("program")]
    [InlineData("pool")] [InlineData("stake")] [InlineData("take")] [InlineData("limit")]
    public void InvalidStructureCannotFreeze(string field)
    {
        var d = Definition(); var race = d.Races[0];
        switch (field)
        {
            case "event": d.RaceDayId = Guid.NewGuid().ToString(); break;
            case "date": d.EventDate = "2027-02-29"; break;
            case "zone": d.TimeZone = "Not/AZone"; break;
            case "currency": d.Currency = "pln"; break;
            case "race": d.Races.Add(race.Clone()); break;
            case "number": race.Number = 0; break;
            case "horse": race.Runners[1].HorseId = race.Runners[0].HorseId; break;
            case "program": race.Runners[1].ProgramNumber = race.Runners[0].ProgramNumber; break;
            case "pool": race.Pools[1].Type = PoolType.Win; break;
            case "stake": race.Pools[0].UnitStake = 0; break;
            case "take": race.Pools[0].TakeBasisPoints = 10001; break;
            case "limit": d.MaximumOpenRaces = 0; break;
        }
        var plan = new LocalRaceDayPlan(d);
        Assert.NotEmpty(plan.Validate([Day], [Day.ToString()]));
        Assert.Throws<InvalidOperationException>(() => plan.Freeze([Day], [Day.ToString()]));
    }
    [Fact] public void ManagedListAloneDoesNotAuthorizeCreate()
    {
        var plan = new LocalRaceDayPlan(Definition());
        Assert.NotEmpty(plan.Validate([Day], []));
        Assert.NotEmpty(plan.Validate([], [Day.ToString()]));
        Assert.False(LocalRaceDayPlan.Deserialize(plan.Serialize()).IsFrozen);
    }
}
