using System.Globalization;
using System.Text.Json;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public static class StaffPreparation
{
    public static bool SameNormalSelection(PoolType type, BettingSelection expected, BettingSelection actual)
    {
        if (expected.Positional is null || actual.Positional is null || expected.Positional.Positions.Count != CashierComposer.Arity(type) ||
            actual.Positional.Positions.Count != CashierComposer.Arity(type) ||
            expected.Positional.Positions.Concat(actual.Positional.Positions).Any(p => p.ChoiceCase != Position.ChoiceOneofCase.HorseId)) return false;
        var a = expected.Positional.Positions.Select(p => p.HorseId); var b = actual.Positional.Positions.Select(p => p.HorseId);
        return type == PoolType.Quinella ? a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal)) : a.SequenceEqual(b);
    }
    // Whole currency units. This form also works at Int64.MaxValue without overflow.
    public static long HalfBalanceStake(long balance) => balance >= 0
        ? balance / 2 + balance % 2 : throw new ArgumentOutOfRangeException(nameof(balance));

    public static RaceDayConfiguration SixRaceTemplate(Guid day, DateOnly date, string currency = "PLN")
    {
        if (day == Guid.Empty) throw new ArgumentException("Event UUID required.", nameof(day));
        var definition = new RaceDayConfiguration {
            RaceDayId = day.ToString("D"), EventDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DisplayNumber = 1, Currency = currency, TimeZone = "Europe/Warsaw", RulesVersion = "1",
            MaximumCouponLegs = 10000, MaximumOpenRaces = 1, NoWinnerPolicy = NoWinnerPolicy.HouseRetainsPool
        };
        for (var number = 1; number <= 6; number++)
        {
            var race = new RaceConfiguration {
                RaceId = Guid.NewGuid().ToString("D"), Number = number,
                Title = $"Race {number}", RacecourseName = "Executive demonstration",
                Pools = { new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }
                    .Select(type => new PoolConfiguration { Type = type, UnitStake = 1, TakeBasisPoints = 1000 }) }
            };
            for (var horse = 1; horse <= (number <= 3 ? 6 : 12); horse++)
                race.Runners.Add(new Runner { HorseId = Guid.NewGuid().ToString("D"),
                    ProgramNumber = horse.ToString("00", CultureInfo.InvariantCulture), DisplayName = $"Race {number} Horse {horse:00}" });
            definition.Races.Add(race);
        }
        return definition;
    }
}

public sealed record PlayerAlias(Guid Player, string DisplayName)
{
    public override string ToString() => $"{DisplayName} · {Player:D}";
}

/// <summary>App-managed labels only; this store never enrolls or grants access to a player.</summary>
public sealed class PlayerAliasStore
{
    private readonly string directory;
    public PlayerAliasStore() : this(ProtectedLocalStorage.DirectoryFor("PlayerAliases")) { }
    internal PlayerAliasStore(string directory) => this.directory = directory;
    private string PathFor(string profile, Guid day)
    {
        if (day == Guid.Empty || string.IsNullOrEmpty(profile)) throw new ArgumentException("Profile and event required.");
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { profile, day })));
        return Path.Combine(directory, key + ".json");
    }
    public IReadOnlyList<PlayerAlias> Load(string profile, Guid day)
    {
        var path = PathFor(profile, day);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("Alias file too large.");
        var rows = JsonSerializer.Deserialize<PlayerAlias[]>(File.ReadAllBytes(path)) ?? throw new IOException("Invalid aliases.");
        Validate(rows);
        return rows;
    }
    private static void Validate(IReadOnlyList<PlayerAlias> rows)
    {
        if (rows.Count > 1000 || rows.Select(p => p.Player).Distinct().Count() != rows.Count ||
            rows.Any(p => p.Player == Guid.Empty || string.IsNullOrWhiteSpace(p.DisplayName) ||
                p.DisplayName.Length > 128 || p.DisplayName.Any(char.IsControl)))
            throw new IOException("Use unique player UUIDs and display names of 1–128 characters.");
    }
    public void Save(string profile, Guid day, IReadOnlyList<PlayerAlias> rows)
    {
        Validate(rows); Directory.CreateDirectory(directory);
        using var writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var path = PathFor(profile, day); var temporary = path + ".pending";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { file.Write(JsonSerializer.SerializeToUtf8Bytes(rows)); file.Flush(true); }
        File.Move(temporary, path, true);
    }
    public IReadOnlyList<PlayerAlias> PrepareGroup(string profile, Guid day, int count = 24)
    {
        if (count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        var existing = Load(profile, day);
        if (existing.Count != 0) return existing; // A repeated click/restart retains the same identities.
        var rows = Enumerable.Range(1, count).Select(i => new PlayerAlias(Guid.NewGuid(), $"Player {i:00}")).ToArray();
        Save(profile, day, rows); return rows;
    }
    public Guid PendingEvent(string profile, Guid? completedEvent = null)
    {
        Directory.CreateDirectory(directory);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile)));
        var path = Path.Combine(directory, name + ".event-intent");
        using var writer = new FileStream(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            if (!Guid.TryParseExact(File.ReadAllText(path), "D", out var saved) || saved == Guid.Empty)
                throw new IOException("Invalid event provisioning intent; investigate before creating another event.");
            if (completedEvent != saved) return saved;
        }
        var day = Guid.NewGuid();
        using (var file = new FileStream(path + ".pending", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { file.Write(System.Text.Encoding.UTF8.GetBytes(day.ToString("D"))); file.Flush(true); }
        File.Move(path + ".pending", path, true); return day;
    }
}
