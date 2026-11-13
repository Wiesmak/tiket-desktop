using System.Globalization;
using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

/// <summary>Local definition only. Provisioned IDs come from trusted identity and managed configuration.
/// Freezing a plan retains one exact request/key; editing cannot replace that request.</summary>
public sealed class LocalRaceDayPlan
{
    private RaceDayConfiguration definition;
    private CreateRaceDayRequest? frozen;
    public LocalRaceDayPlan(RaceDayConfiguration definition) => this.definition = definition.Clone();
    public RaceDayConfiguration Definition => definition.Clone();
    public bool IsFrozen => frozen is not null;
    public void Replace(RaceDayConfiguration value)
    {
        if (IsFrozen) throw new InvalidOperationException("Created or pending definitions cannot be edited.");
        definition = value.Clone();
    }
    public void MoveRace(int from, int to)
    {
        if (IsFrozen) throw new InvalidOperationException("Frozen plans cannot be reordered.");
        if (from < 0 || to < 0 || from >= definition.Races.Count || to >= definition.Races.Count) throw new ArgumentOutOfRangeException();
        var race = definition.Races[from]; definition.Races.RemoveAt(from); definition.Races.Insert(to, race);
        // The server persists race order by number; UUIDs and each roster stay unchanged.
        for (int i = 0; i < definition.Races.Count; i++) definition.Races[i].Number = i + 1;
    }
    public IReadOnlyList<string> Validate(IReadOnlyCollection<Guid> managedSlots, IReadOnlyCollection<string> trustedEventGrants)
    {
        var errors = new List<string>();
        if (!Guid.TryParseExact(definition.RaceDayId, "D", out var day) || day == Guid.Empty ||
            !managedSlots.Contains(day) || !trustedEventGrants.Contains(definition.RaceDayId))
            errors.Add("Select an unused event slot provisioned in your identity and managed profile.");
        if (!DateOnly.TryParseExact(definition.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add("Enter a date as yyyy-MM-dd.");
        if (definition.DisplayNumber is < 1 or > 999) errors.Add("Display number must be 1–999.");
        if (definition.Currency.Length != 3 || definition.Currency.Any(c => c is < 'A' or > 'Z'))
            errors.Add("Enter a three-letter uppercase ISO currency.");
        try
        {
            if (definition.TimeZone != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(definition.TimeZone, out _))
                errors.Add("Select an IANA time zone.");
            else _ = TimeZoneInfo.FindSystemTimeZoneById(definition.TimeZone);
        }
        catch (TimeZoneNotFoundException) { errors.Add("Select an installed IANA time zone."); }
        catch (InvalidTimeZoneException) { errors.Add("Select a valid IANA time zone."); }
        if (string.IsNullOrWhiteSpace(definition.RulesVersion)) errors.Add("Rules version is required.");
        if (definition.MaximumCouponLegs <= 0 || definition.MaximumOpenRaces <= 0)
            errors.Add("Coupon leg and open-race limits must be positive.");
        if (definition.NoWinnerPolicy != NoWinnerPolicy.HouseRetainsPool) errors.Add("Select the supported no-winner policy.");
        if (definition.Races.Count == 0) errors.Add("Add at least one race.");
        var raceIds = new HashSet<Guid>(); var numbers = new HashSet<int>();
        foreach (var race in definition.Races)
        {
            var prefix = $"Race {race.Number}: ";
            if (!Guid.TryParseExact(race.RaceId, "D", out var id) || id == Guid.Empty || !raceIds.Add(id))
                errors.Add(prefix + "race UUID must be unique and nonempty.");
            if (race.Number <= 0 || !numbers.Add(race.Number)) errors.Add(prefix + "number must be positive and unique.");
            if (string.IsNullOrWhiteSpace(race.Title) || string.IsNullOrWhiteSpace(race.RacecourseName))
                errors.Add(prefix + "title and course are required.");
            if (race.Runners.Count == 0) errors.Add(prefix + "add runners.");
            var horseIds = new HashSet<Guid>(); var programs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var runner in race.Runners)
            {
                if (!Guid.TryParseExact(runner.HorseId, "D", out var horse) || horse == Guid.Empty || !horseIds.Add(horse))
                    errors.Add(prefix + "horse UUIDs must be unique and nonempty.");
                if (string.IsNullOrWhiteSpace(runner.ProgramNumber) || !programs.Add(runner.ProgramNumber))
                    errors.Add(prefix + "program labels must be nonempty and unique.");
                if (string.IsNullOrWhiteSpace(runner.DisplayName)) errors.Add(prefix + "runner name is required.");
            }
            if (race.Pools.Count != 4 || !race.Pools.Select(p => p.Type).Order().SequenceEqual(
                    new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }))
                errors.Add(prefix + "configure WIN, QUINELLA, EXACTA and TRIFECTA exactly once.");
            if (race.Pools.Any(p => p.UnitStake <= 0 || p.TakeBasisPoints is < 0 or > 10000))
                errors.Add(prefix + "stakes must be positive int64 units; take must be 0–10000 basis points.");
        }
        return errors;
    }
    public CreateRaceDayRequest Freeze(IReadOnlyCollection<Guid> managedSlots, IReadOnlyCollection<string> trustedEventGrants)
    {
        if (frozen is not null) return frozen.Clone();
        var errors = Validate(managedSlots, trustedEventGrants);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        frozen = new() { Definition = definition.Clone(), IdempotencyKey = Guid.NewGuid().ToString("D") };
        return frozen.Clone();
    }
    // Persist both editable definition and frozen exact wire payload; never regenerate a pending key on resume.
    public byte[] Serialize() => frozen is null ? new byte[] { 1, 0 }.Concat(definition.ToByteArray()).ToArray() :
        new byte[] { 1, 1 }.Concat(frozen.ToByteArray()).ToArray();
    public static LocalRaceDayPlan Deserialize(byte[] bytes)
    {
        if (bytes.Length < 3 || bytes.Length > 4 * 1024 * 1024 || bytes[0] != 1 || bytes[1] > 1)
            throw new IOException("Unsupported local plan.");
        if (bytes[1] == 0) return new(RaceDayConfiguration.Parser.ParseFrom(bytes[2..]));
        var request = CreateRaceDayRequest.Parser.ParseFrom(bytes[2..]);
        if (request.Definition is null || !Guid.TryParseExact(request.IdempotencyKey, "D", out _))
            throw new IOException("Incomplete frozen local plan.");
        return new(request.Definition) { frozen = request.Clone() };
    }
}
