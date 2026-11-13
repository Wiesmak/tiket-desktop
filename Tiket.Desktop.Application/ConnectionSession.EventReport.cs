using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;
public sealed partial class ConnectionSession
{
    public async Task<EventPayoutReport> BuildEventReportAsync(IReadOnlyList<PlayerAlias> aliases, Action<string> progress, CancellationToken cancellation)
    {
        if (StaffWorkflowBusy || !IsAuditor || selectedEvent is not { } day || Overview is null) throw new InvalidOperationException("Load an event with auditor access.");
        var binding = AdministrationContext(day.ToString()); var overview = Overview; var actor = generation;
        backend ??= backendFactory(Profile!);
        if (backend is not IResultsBackend results || backend is not IAdministrationBackend admin || backend is not IAccountBackend accounts || backend is not IAccountHistoryBackend history)
            throw new InvalidOperationException("Report transports unavailable.");
        StaffWorkflowBusy = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, eventLifetime.Token);
        var ct = linked.Token; var token = material!.AccessToken;
        try
        {
            EnsureAdministrationStorage();
            var saved = administrationJournal!.Read(binding).Where(r => r.Method == AdministrationWire.Prefix + "ResultService/SettleRace" && r.State == MutationState.Acked)
                .Select(r => SettlementInformation.Parser.ParseFrom(r.Response!)).ToArray();
            var races = new List<EventRacePayout>();
            foreach (var race in overview.Races.Races)
            {
                progress($"Reading race {race.Definition.Number} settlements and winning legs…");
                var id = race.Definition.RaceId; OfficialResult? official = null; WinnersReport? report = null;
                if (race.Status.Phase == RacePhase.Settled)
                {
                    official = (await results.GetResultAsync(new() { RaceDayId = binding.Event, RaceId = id }, token, ct)).Result;
                    var all = new List<TerminalLegResolution>(); var offset = 0;
                    for (var page = 0; ; page++)
                    {
                        var response = await results.ListResolutionsAsync(new() { Race = new() { RaceDayId = binding.Event, RaceId = id }, PageSize = 500, Offset = offset }, token, ct);
                        all.AddRange(response.Resolutions); if (!response.HasNextOffset) break;
                        if (response.NextOffset <= offset || page >= 199) throw new IOException("Incomplete resolution pagination.");
                        offset = response.NextOffset;
                    }
                    var settlementId = saved.FirstOrDefault(s => s.Status.RaceId == id)?.SettlementId ?? all.Select(r => r.SettlementId).Distinct().SingleOrDefault();
                    SettlementInformation? settlement = settlementId is null ? null : await results.GetSettlementAsync(new() { RaceDayId = binding.Event, RaceId = id, SettlementId = settlementId }, token, ct);
                    if (official.RaceDayId != binding.Event || official.RaceId != id || official.Revision != race.Status.SelectedResultRevision ||
                        (settlement is not null && (settlement.Status.RaceDayId != binding.Event || settlement.Status.RaceId != id || settlement.ResultRevision != official.Revision)))
                        throw new IOException("Result/settlement identity mismatch.");
                    var coupons = new Dictionary<string, CouponInformation>();
                    foreach (var coupon in all.Where(r => r.Outcome == TerminalLegOutcome.Won).Select(r => r.CouponId).Distinct())
                    {
                        await Task.Delay(40, ct); // Bound event-wide reads below the lab's 60 requests/second limit.
                        coupons[coupon] = await results.GetWinningCouponAsync(new() { RaceDayId = binding.Event, CouponId = coupon }, token, ct);
                    }
                    report = WinnersReport.Build(binding.Event, id, overview.Day.Definition.Currency, all, coupons, settlement, true);
                }
                races.Add(new(race.Clone(), official, report));
            }
            var roster = await admin.ListPlayersAsync(new() { RaceDayId = binding.Event }, token, ct);
            var players = new List<EventPlayerPayout>();
            foreach (var player in roster.Players)
            {
                await Task.Delay(75, ct);
                progress($"Reading authoritative accounts {players.Count + 1}/{roster.Players.Count}…");
                var q = new AccountQuery { RaceDayId = binding.Event, PlayerId = player.PlayerId };
                var account = await accounts.GetAccountAsync(q, token, ct);
                WithdrawalQuoteInformation? quote = null; CashoutReceipt? receipt = null;
                if (account.CashedOut) receipt = await history.ReceiptAsync(q, token, ct);
                else if (overview.Day.Status.Phase == DayPhase.Completed) quote = await history.QuoteAsync(q, token, ct);
                if (account.PlayerId != player.PlayerId || (receipt is not null && (receipt.PlayerId != player.PlayerId || receipt.RaceDayId != binding.Event || receipt.Currency != account.Currency)))
                    throw new IOException("Account/receipt identity mismatch.");
                players.Add(new(aliases.FirstOrDefault(a => a.Player.ToString("D") == player.PlayerId)?.DisplayName ?? "No local alias", account, quote, receipt));
            }
            if (actor != generation || selectedEvent != day) throw new OperationCanceledException();
            var completedReport = EventPayoutReport.Build(overview, races, players);
            if (aliases.Any(a => !players.Any(p => p.Account.PlayerId == a.Player.ToString("D"))))
                completedReport = completedReport with { Complete = false, Status = "Partial · prepared roster contains players absent from the authoritative enrollment list" };
            return completedReport;
        }
        finally { StaffWorkflowBusy = false; }
    }
}
