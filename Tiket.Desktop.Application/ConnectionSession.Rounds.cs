using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public sealed record RoundEntry(Guid Id, Guid Coupon, Guid Key, PlayerAlias Player, long Balance, long Stake, byte[]? Quote);
public sealed class RaceRound
{
    [System.Text.Json.Serialization.JsonInclude] internal MutationBinding Binding { get; }
    public string Race { get; }
    public RoundEntry[] Entries { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    internal RaceRound(MutationBinding binding, string race, RoundEntry[] entries) { Binding = binding; Race = race; Entries = entries; }
}

public sealed partial class ConnectionSession
{
    public bool StaffWorkflowBusy { get; private set; }
    private string RoundPath(MutationBinding binding, string race)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { binding, race })));
        return Path.Combine(ProtectedLocalStorage.DirectoryFor("RaceRounds"), key + ".round.dpapi");
    }
    public RaceRound? ResumeRound(string race, int pos)
    {
        var day = Overview?.Day.Definition.RaceDayId ?? throw new InvalidOperationException("Load the event first.");
        var binding = Binding(day, pos); var path = RoundPath(binding, race);
        if (!File.Exists(path)) return null;
        var round = JsonSerializer.Deserialize<RaceRound>(new CurrentUserJournalProtection().Unprotect(File.ReadAllBytes(path))) ?? throw new IOException("Invalid saved round.");
        if (round.Binding != binding || round.Race != race || round.Entries.Select(r => r.Id).Distinct().Count() != round.Entries.Length)
            throw new IOException("Round context mismatch.");
        return round;
    }
    public async Task<RaceRound> PlanRoundAsync(string raceId, int pos, IReadOnlyList<PlayerAlias> roster, Action<string> progress, CancellationToken cancellation)
    {
        if (StaffWorkflowBusy) throw new InvalidOperationException("Finish the current staff operation first.");
        var existing = ResumeRound(raceId, pos); if (existing is not null) return existing;
        var day = Overview!.Day; var race = Overview.Races.Races.Single(r => r.Definition.RaceId == raceId);
        if (day.Status.Phase != DayPhase.Running || race.Status.Phase != RacePhase.BettingOpen || roster.Count == 0 || roster.Select(r => r.Player).Distinct().Count() != roster.Count)
            throw new InvalidOperationException("Open betting and prepare a unique player roster first.");
        var binding = Binding(day.Definition.RaceDayId, pos); var actor = generation;
        backend ??= backendFactory(Profile!);
        if (backend is not IAccountBackend accounts) throw new InvalidOperationException("Account transport required.");
        var horses = race.Definition.Runners.Where(h => !race.Status.ScratchedHorseIds.Contains(h.HorseId)).ToArray();
        if (horses.Length < 3 || race.Definition.Pools.Any(p => p.UnitStake != 1)) throw new InvalidOperationException("Half-balance rounds require unit stake 1 and at least three eligible horses.");
        StaffWorkflowBusy = true;
        try
        {
            var rows = new List<RoundEntry>();
            for (var i = 0; i < roster.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (actor != generation || selectedEvent?.ToString() != binding.Event) throw new OperationCanceledException();
                var player = roster[i];
                if (material!.OperatingGrants?.Players.Contains(player.Player.ToString("D")) != true) throw new InvalidOperationException("Renew signed access for every roster player first.");
                var account = await accounts.GetAccountAsync(new() { RaceDayId = binding.Event, PlayerId = player.Player.ToString("D") }, material.AccessToken, cancellation);
                if (account.RaceDayId != binding.Event || account.PlayerId != player.Player.ToString("D") || account.Currency != day.Definition.Currency || account.CashedOut)
                    throw new IOException("Account identity/currency mismatch or account closed.");
                var stake = StaffPreparation.HalfBalanceStake(account.Balance); QuoteCouponResponse? quote = null;
                if (stake > 0)
                {
                    var type = new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }[i % 4];
                    var arity = CashierComposer.Arity(type);
                    var choices = i % 8 < 4 ? horses.Take(arity) : horses.Reverse().Take(arity);
                    var selection = new BettingSelection { Positional = new() { Positions = { choices.Select(h => new Position { HorseId = h.HorseId }) } } };
                    quote = await backend.QuoteAsync(new() { RaceDayId = binding.Event, RaceId = raceId, BetType = type, Selection = selection, UnitsPerCombination = stake }, material.AccessToken, cancellation);
                    if (quote.Pricing.CombinationCount != 1 || quote.Pricing.RaceDayId != binding.Event || quote.Pricing.RaceId != raceId || quote.Pricing.TotalStake != stake || quote.Pricing.Currency != account.Currency ||
                        quote.Pricing.UnitsPerCombination != stake || !StaffPreparation.SameNormalSelection(type, selection, quote.Pricing.Selection) || quote.Pricing.BetType != type || string.IsNullOrEmpty(quote.Confirmation))
                        throw new IOException($"Quote mismatch for {player.DisplayName}: requested {type}, units/stake {stake}; server {quote.Pricing.BetType}, units {quote.Pricing.UnitsPerCombination}, stake {quote.Pricing.TotalStake}, combinations {quote.Pricing.CombinationCount}, currency {quote.Pricing.Currency}; selection {CashierComposer.Canonical(quote.Pricing.Selection, horses)}. No purchase sent.");
                }
                rows.Add(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), player, account.Balance, stake, quote?.ToByteArray()));
                progress($"Quoted {rows.Count}/{roster.Count} · {player.DisplayName}");
            }
            if (actor != generation || selectedEvent?.ToString() != binding.Event) throw new OperationCanceledException();
            var round = new RaceRound(binding, raceId, rows.ToArray()); var bytes = new CurrentUserJournalProtection().Protect(JsonSerializer.SerializeToUtf8Bytes(round));
            using var file = new FileStream(RoundPath(binding, raceId), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            file.Write(bytes); file.Flush(true); return round;
        }
        finally { StaffWorkflowBusy = false; }
    }
    public string RoundSummary(RaceRound round)
    {
        if (Binding(round.Binding.Event, round.Binding.Pos) != round.Binding) throw new InvalidOperationException("Restore the original round operator and POS.");
        EnsureJournal(); var records = journal!.Read(round.Binding).ToDictionary(r => r.Id);
        var lines = round.Entries.Select(row => {
            var quote = row.Quote is null ? null : QuoteCouponResponse.Parser.ParseFrom(row.Quote);
            var state = records.TryGetValue(row.Id, out var record) ? record.State.ToString() : row.Stake == 0 ? "Bankrupt · skip" : "Quoted · pending";
            return $"{row.Player}\nServer balance {row.Balance} · target {row.Stake} · {state}" + (quote is null ? "" :
                $"\n{quote.Pricing.BetType} · {CashierComposer.Canonical(quote.Pricing.Selection, Overview!.Races.Races.Single(r => r.Definition.RaceId == round.Race).Definition.Runners)} · server quote {quote.Pricing.TotalStake} {quote.Pricing.Currency}");
        });
        return $"Event {round.Binding.Event} · Race {Overview?.Races.Races.FirstOrDefault(r => r.Definition.RaceId == round.Race)?.Definition.Number} · {round.Race}\nPolicy: ceil(current server balance / 2), whole units; only zero balances are skipped.\n" + string.Join("\n\n", lines) +
            $"\n\nConfirmed {round.Entries.Count(e => records.TryGetValue(e.Id, out var r) && r.State == MutationState.Acked)}/{round.Entries.Count(e => e.Stake > 0)} · quoted stake {round.Entries.Sum(e => e.Stake)}";
    }
    public async Task PlaceRoundAsync(RaceRound round, Action<string> progress, CancellationToken cancellation)
    {
        if (StaffWorkflowBusy || Binding(round.Binding.Event, round.Binding.Pos) != round.Binding) throw new InvalidOperationException("Round context unavailable.");
        EnsureJournal(); var actor = generation; StaffWorkflowBusy = true;
        try
        {
            foreach (var row in round.Entries.Where(e => e.Stake > 0))
            {
                cancellation.ThrowIfCancellationRequested();
                if (actor != generation || selectedEvent?.ToString() != round.Binding.Event) throw new OperationCanceledException();
                var records = journal!.Read(round.Binding);
                if (records.Any(r => r.State != MutationState.Acked)) throw new InvalidOperationException("Stop: explicitly recover the original pending/UNKNOWN command before resuming this round.");
                if (records.Any(r => r.Id == row.Id)) continue;
                var quote = QuoteCouponResponse.Parser.ParseFrom(row.Quote!); var p = quote.Pricing;
                var request = new PurchaseCouponRequest { RaceDayId = round.Binding.Event, RaceId = round.Race, PlayerId = row.Player.Player.ToString("D"),
                    CouponId = row.Coupon.ToString("D"), IdempotencyKey = row.Key.ToString("D"), PointOfSaleNumber = round.Binding.Pos,
                    CashierIdentifier = material!.OperatingGrants!.Cashier, BetType = p.BetType, Selection = p.Selection.Clone(), UnitsPerCombination = p.UnitsPerCombination,
                    QuoteConfirmation = quote.Confirmation, ExpectedDayVersion = quote.RaceDayVersion, ExpectedRaceVersion = quote.RaceVersion };
                var record = new MutationRecord(1, row.Id, round.Binding, "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon", request.ToByteArray(), request.CouponId,
                    request.IdempotencyKey, request.PlayerId, request.CashierIdentifier, quote.ToByteArray(), DateTimeOffset.UtcNow, MutationState.Prepared, null, null);
                RequireRecordRights(record); journal.Prepare(record); await SubmitAsync(record);
                progress(RoundSummary(round));
                if (journal.Read(round.Binding).Single(r => r.Id == row.Id).State != MutationState.Acked)
                    throw new InvalidOperationException("Round stopped on an unconfirmed purchase. Preserve and explicitly recover its original request.");
            }
        }
        finally { StaffWorkflowBusy = false; }
    }
}
