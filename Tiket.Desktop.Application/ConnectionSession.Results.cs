using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;

public sealed partial class ConnectionSession
{
    private string? resultRace;
    private string? settlementReviewRevision;
    private long resultsRead;
    public ResultAcknowledgement? LatestResult { get; private set; }
    public WinnersReport? Winners { get; private set; }
    public string ResultsStatus { get; private set; } = "";
    public bool HasResultRole(string role) => material?.AdministrationGrants?.Roles.Contains(role) == true;
    private void ResetResults() { resultsRead++; resultRace = null; LatestResult = null; Winners = null; ResultsStatus = ""; settlementReviewRevision = null; }
    public void SelectResultRace(string? race)
    {
        if (resultRace == race) return;
        ResetResults(); resultRace = race;
        administrationReview = null; RefreshRequested?.Invoke();
    }
    public AdministrationReview ReviewResultOperation(AdministrationOperation operation, string raceId, string revision = "",
        OfficialResultKind kind = OfficialResultKind.Normal, IEnumerable<string>? finishers = null, string? horse = null)
    {
        var day = Overview?.Day ?? throw new InvalidOperationException("Load the event.");
        var race = Overview!.Races.Races.Single(r => r.Definition.RaceId == raceId);
        if (day.Status.Phase is not (DayPhase.Running or DayPhase.Finalizing)) throw new InvalidOperationException("Day is not running.");
        var c = new RaceCommand { RaceDayId = day.Definition.RaceDayId, RaceId = raceId, ExpectedDayVersion = day.Status.Version,
            ExpectedRaceVersion = race.Status.Version, IdempotencyKey = Guid.NewGuid().ToString() };
        IMessage request;
        var phase = race.Status.Phase;
        if (operation is AdministrationOperation.ScratchHorse or AdministrationOperation.VoidRace) {
            if (phase is not (RacePhase.Scheduled or RacePhase.BettingOpen or RacePhase.BettingClosed or RacePhase.ResultProposed))
                throw new InvalidOperationException("Refund operations require a race before verification.");
            if (operation == AdministrationOperation.ScratchHorse) {
                if (!race.Definition.Runners.Any(r => r.HorseId == horse) || race.Status.ScratchedHorseIds.Contains(horse!))
                    throw new InvalidOperationException("Select an eligible horse.");
                request = new ScratchHorseRequest { Command = c, HorseId = horse };
            } else request = c;
        } else if (operation == AdministrationOperation.SubmitResult) {
            if (phase is not (RacePhase.BettingClosed or RacePhase.ResultProposed)) throw new InvalidOperationException("Close betting before proposing.");
            if (string.IsNullOrWhiteSpace(revision) || revision.Length > 128 || kind is not (OfficialResultKind.Normal or OfficialResultKind.Exceptional))
                throw new InvalidOperationException("Enter a revision and result kind.");
            var ordered = finishers?.ToArray() ?? [];
            if (ordered.Distinct().Count() != ordered.Length || ordered.Any(id => !race.Definition.Runners.Any(r => r.HorseId == id) || race.Status.ScratchedHorseIds.Contains(id)) ||
                (kind == OfficialResultKind.Normal && ordered.Length < 3)) throw new InvalidOperationException("Choose at least three distinct eligible finishers in order for a normal result.");
            request = new SubmitResultRequest { Command = c, Revision = revision, Kind = kind, FinisherIds = { ordered } };
        } else if (operation == AdministrationOperation.VerifyResult) {
            if (phase != RacePhase.ResultProposed || LatestResult?.Result?.Revision != race.Status.SelectedResultRevision)
                throw new InvalidOperationException("Load the current proposal.");
            request = new VerifyResultRequest { Command = c, Revision = race.Status.SelectedResultRevision };
        } else if (operation == AdministrationOperation.SettleRace) {
            if (phase != RacePhase.ResultVerified || LatestResult?.Result?.Revision != race.Status.SelectedResultRevision)
                throw new InvalidOperationException("Load the verified result.");
            settlementReviewRevision = race.Status.SelectedResultRevision;
            request = new SettleRaceRequest { Command = c, SettlementId = Guid.NewGuid().ToString() };
        } else throw new InvalidOperationException("Unsupported action.");
        var review = ReviewAdministration(operation, request, AdministrationContext(c.RaceDayId));
        return operation == AdministrationOperation.SettleRace ?
            review with { Payload = "Final and irreversible · confirmed result revision " + settlementReviewRevision + "\n\n" + review.Payload } : review;
    }
    public async Task<bool> ReadResultsAsync(CancellationToken cancellation)
    {
        if (resultRace is not { } raceId || selectedEvent is not { } day || material is null || mutationBusy) return true;
        var binding = AdministrationContext(day.ToString());
        var race = Overview?.Races.Races.SingleOrDefault(r => r.Definition.RaceId == raceId);
        if (race is null) return true;
        var stamp = ++resultsRead; var actor = generation; var context = eventRevision;
        backend ??= backendFactory(Profile!);
        if (backend is not IResultsBackend transport) return false;
        var token = material.AccessToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, eventLifetime.Token);
        bool Current() => stamp == resultsRead && actor == generation && context == eventRevision && !linked.IsCancellationRequested;
        try {
            ResultAcknowledgement? result = null;
            if (race.Status.Phase is RacePhase.ResultProposed or RacePhase.ResultVerified or RacePhase.Settled) {
                result = await transport.GetResultAsync(new() { RaceDayId = binding.Event, RaceId = raceId }, token, linked.Token);
                if (result.Status?.RaceId != raceId || result.Status.RaceDayId != binding.Event || result.Result?.Revision != race.Status.SelectedResultRevision)
                    throw new IOException("Result changed during read.");
            }
            WinnersReport? report = null;
            var status = race.Status.Phase == RacePhase.Voided ? "Voided · remaining active legs refunded" : "Awaiting settlement";
            if (race.Status.Phase == RacePhase.Settled && IsAuditor) {
                var all = new List<TerminalLegResolution>(); var offset = 0; var complete = true;
                for (var page = 0; ; page++) {
                    var response = await transport.ListResolutionsAsync(new() { Race = new() { RaceDayId = binding.Event, RaceId = raceId }, PageSize = 500, Offset = offset }, token, linked.Token);
                    all.AddRange(response.Resolutions);
                    if (!response.HasNextOffset) break;
                    if (response.NextOffset <= offset || page >= 199) { complete = false; break; }
                    offset = response.NextOffset;
                }
                EnsureAdministrationStorage();
                var saved = administrationJournal!.Read(binding).Where(r => r.Method == AdministrationWire.Prefix + "ResultService/SettleRace" && r.State == MutationState.Acked)
                    .Select(r => SettlementInformation.Parser.ParseFrom(r.Response!)).FirstOrDefault(s => s.Status.RaceId == raceId);
                var ids = all.Select(r => r.SettlementId).Where(id => Guid.TryParseExact(id, "D", out _)).Distinct().ToArray();
                var settlementId = saved?.SettlementId ?? (ids.Length == 1 ? ids[0] : null);
                SettlementInformation? settlement = null;
                if (settlementId is not null) {
                    settlement = await transport.GetSettlementAsync(new() { RaceDayId = binding.Event, RaceId = raceId, SettlementId = settlementId }, token, linked.Token);
                    if (settlement.SettlementId != settlementId || settlement.Status?.RaceDayId != binding.Event || settlement.Status.RaceId != raceId ||
                        settlement.Status.Phase != RacePhase.Settled || settlement.ResultRevision != race.Status.SelectedResultRevision)
                        throw new IOException("Settlement identity mismatch.");
                }
                // Sequential reads bound concurrency to one; cache each distinct winning coupon for this snapshot.
                var coupons = new Dictionary<string, CouponInformation>();
                foreach (var id in all.Where(r => r.Outcome == TerminalLegOutcome.Won).Select(r => r.CouponId).Distinct()) {
                    try { coupons[id] = await transport.GetWinningCouponAsync(new() { RaceDayId = binding.Event, CouponId = id }, token, linked.Token); }
                    catch (RpcException e) when (e.StatusCode is StatusCode.PermissionDenied or StatusCode.NotFound) { complete = false; }
                }
                report = WinnersReport.Build(binding.Event, raceId, Overview!.Day.Definition.Currency, all, coupons, settlement, complete);
                status = report.Status;
            } else if (!IsAuditor) status = "Auditor access required for Winners & Payouts";
            CheckExpiry(); if (!Current()) return false;
            LatestResult = result; Winners = report; ResultsStatus = status; Changed?.Invoke(); return true;
        } catch (OperationCanceledException) { return false; }
        catch (Exception e) {
            if (!Current()) return false;
            LatestResult = null; Winners = null;
            ResultsStatus = e is RpcException rpc && rpc.StatusCode == StatusCode.PermissionDenied ? "Access denied · report unavailable" : "Result/report unavailable";
            if (e is RpcException unauth && unauth.StatusCode == StatusCode.Unauthenticated) { Clear(); State = ConnectionState.Expired; }
            Changed?.Invoke(); return false;
        }
    }
}
