using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public enum AdministrationOperation { CreateRaceDay, PublishRaceDay, StartRaceDay, OpenBetting, CloseBetting, EnrollPlayer, AddOpeningFunding, ScratchHorse, VoidRace, SubmitResult, VerifyResult, SettleRace, FinalizeRaceDay, CompleteRaceDay, CloseRaceDay, CashOut }
public sealed record AdministrationReview(Guid Id, string Title, string Payload);
public sealed record LocalAdministration(Guid Id, string Operation, string State, bool CanRecover);

public sealed partial class ConnectionSession
{
    private AdministrationJournal? administrationJournal;
    private LocalPlanStore? planStore;
    private (long Actor, long Context, AdministrationRecord Record)? administrationReview;
    private Guid? selectedEvent;
    private long eventRevision;
    private CancellationTokenSource eventLifetime = new();
    private EnrollmentInformation? enrollment;
    private AccountInformation? account;
    private PlayerList? players;
    private string? selectedPlayer;
    private bool playerReadDenied;
    public string AdministrationStatus { get; private set; } = "";
    public Guid? UnusedEvent { get; private set; }
    public bool IsEventAdmin => material?.AdministrationGrants?.Roles.Contains("EVENT_ADMIN") == true;
    public bool IsAuditor => material?.AdministrationGrants?.Roles.Contains("AUDITOR") == true;
    public bool IsEnrollmentCashier => material?.AdministrationGrants?.Roles.Contains("CASHIER") == true;
    public EnrollmentInformation? Enrollment => enrollment?.Clone();
    public string AccountReadStatus { get; private set; } = "";
    public DateTimeOffset? AccountReadAt { get; private set; }
    public AccountInformation? CurrentAccount => account?.Clone();
    public string PlayerAccountSummary { get; private set; } = "";
    public PlayerList? EnrolledPlayers => players?.Clone();
    public IReadOnlyList<Guid> ProvisionedEvents => Profile?.KnownEvents.Where(id =>
        material?.AdministrationGrants?.Events.Contains(id.ToString()) == true).ToArray() ?? [];
    public event Action? RefreshRequested;

    private AdministrationBinding AdministrationContext(string day)
    {
        CheckExpiry();
        if (disposed || Profile is null || material?.AdministrationGrants is not { } grants ||
            grants.Subject != Subject || !grants.Events.Contains(day) ||
            !Guid.TryParseExact(day, "D", out var id) || !Profile.KnownEvents.Contains(id) || selectedEvent != id)
            throw new InvalidOperationException("Select an authorized event.");
        return new(Profile.Id, Profile.Backend.OriginalString, Profile.Issuer.OriginalString, Profile.ClientId, grants.Subject, day);
    }
    private void AdministrationRights(AdministrationRecord r)
    {
        if (AdministrationContext(r.Binding.Event) != r.Binding) throw new InvalidOperationException("Original operator and connection required.");
        var grants = material!.AdministrationGrants!;
        if (r.Method == AdministrationWire.Prefix + "AccountService/CashOut")
        {
            var cashout = CashOutRequest.Parser.ParseFrom(r.Request);
            if (!grants.Roles.Contains("CASHIER") || !grants.Players.Contains(cashout.PlayerId)) throw new InvalidOperationException("Cashier access for this player is required.");
        }
        else if (r.Method.Contains("EnrollmentService/", StringComparison.Ordinal))
        {
            var player = r.Method.EndsWith("/EnrollPlayer", StringComparison.Ordinal) ?
                EnrollPlayerRequest.Parser.ParseFrom(r.Request).PlayerId : AddOpeningFundingRequest.Parser.ParseFrom(r.Request).PlayerId;
            if (!grants.Roles.Contains("CASHIER") || !grants.Players.Contains(player))
                throw new InvalidOperationException("Cashier access for this player is required.");
        }
        else if (!grants.Roles.Contains(Phase6Wire.RequiredRole(r.Method))) throw new InvalidOperationException("Required role: " + Phase6Wire.RequiredRole(r.Method));
    }
    private void EnsureAdministrationStorage()
    {
        administrationJournal ??= new AdministrationJournal(ProtectedLocalStorage.DirectoryFor("AdministrationJournal"));
    }
    internal void UseAdministrationStorage(AdministrationJournal value, string draftsDirectory)
    { administrationJournal = value; planStore = new LocalPlanStore(draftsDirectory); }
    private LocalPlanStore Plans => planStore ??= new LocalPlanStore(ProtectedLocalStorage.DirectoryFor("LocalPlans"));
    public void SavePlan(LocalRaceDayPlan plan)
    {
        var binding = AdministrationContext(plan.Definition.RaceDayId);
        if (!IsEventAdmin) throw new InvalidOperationException("Event administrator access required.");
        Plans.Save(binding, plan);
    }
    public LocalRaceDayPlan? ResumePlan(Guid day)
    {
        var binding = AdministrationContext(day.ToString());
        if (!IsEventAdmin) throw new InvalidOperationException("Event administrator access required.");
        return Plans.Load(binding);
    }
    public void DiscardPlan(Guid day)
    {
        var binding = AdministrationContext(day.ToString());
        if (!IsEventAdmin) throw new InvalidOperationException("Event administrator access required.");
        Plans.Discard(binding);
    }
    public void SelectEvent(Guid day)
    {
        if (selectedEvent == day) return;
        ResetEvent();
        selectedEvent = day; if (material is not null) State = ConnectionState.Connecting; Changed?.Invoke();
    }
    private void ResetEvent()
    {
        ResetResults(); ResetAccounts();
        inspectedPrintJobs.Clear();
        eventRevision++; eventLifetime.Cancel(); eventLifetime.Dispose(); eventLifetime = new();
        administrationReview = null; UnusedEvent = null; selectedEvent = null;
        PlayerAccountSummary = ""; enrollment = null; account = null; players = null; selectedPlayer = null; playerReadDenied = false;
        AccountReadStatus = ""; AccountReadAt = null; AdministrationStatus = ""; PurchaseStatus = material?.OperatingGrants is null ? "" : "Ready";
        if (material is not null) State = ConnectionState.SignedIn;
        Overview = null; LastSynchronizedAt = null;
        review = null; Composer.SetContext(null, null);
    }
    public void SelectPlayer(string? player)
    {
        var next = Guid.TryParseExact(player, "D", out var id) ? id.ToString() : null;
        if (next == selectedPlayer) return;
        ResetAccounts(); selectedPlayer = next; enrollment = null; account = null; playerReadDenied = false; AccountReadStatus = next is null ? "" : "Loading account…"; AccountReadAt = null;
        // A different account requires a new financial review, but does not alter a wager draft.
        review = null; administrationReview = null; Changed?.Invoke(); RefreshRequested?.Invoke();
    }
    public AdministrationReview ReviewCreate(LocalRaceDayPlan plan)
    {
        var binding = AdministrationContext(plan.Definition.RaceDayId);
        if (!IsEventAdmin || UnusedEvent?.ToString() != binding.Event)
            throw new InvalidOperationException("Choose a provisioned unused event slot.");
        var request = plan.Freeze(Profile!.KnownEvents, material!.AdministrationGrants!.Events);
        SavePlan(plan); // Frozen exact request survives restart before a review can be confirmed.
        return ReviewAdministration(AdministrationOperation.CreateRaceDay, request, binding);
    }
    public AdministrationReview ReviewDay(AdministrationOperation operation)
    {
        if (operation is not (AdministrationOperation.PublishRaceDay or AdministrationOperation.StartRaceDay or AdministrationOperation.FinalizeRaceDay or AdministrationOperation.CompleteRaceDay or AdministrationOperation.CloseRaceDay))
            throw new ArgumentException("Unsupported day operation.");
        var day = Overview?.Day ?? throw new InvalidOperationException("Current day required.");
        var expected = operation switch { AdministrationOperation.PublishRaceDay => DayPhase.Draft, AdministrationOperation.StartRaceDay => DayPhase.Ready, AdministrationOperation.FinalizeRaceDay => DayPhase.Running, AdministrationOperation.CompleteRaceDay => DayPhase.Finalizing, _ => DayPhase.Completed };
        if (day.Status.Phase != expected) throw new InvalidOperationException("Day phase changed.");
        return ReviewAdministration(operation, new DayCommand { RaceDayId = day.Definition.RaceDayId,
            ExpectedVersion = day.Status.Version, IdempotencyKey = Guid.NewGuid().ToString() }, AdministrationContext(day.Definition.RaceDayId));
    }
    public AdministrationReview ReviewRace(AdministrationOperation operation, string raceId)
    {
        if (operation is not (AdministrationOperation.OpenBetting or AdministrationOperation.CloseBetting))
            throw new ArgumentException("Unsupported race operation.");
        var day = Overview?.Day ?? throw new InvalidOperationException("Current day required.");
        var race = Overview!.Races.Races.Single(r => r.Definition.RaceId == raceId);
        if (day.Status.Phase != DayPhase.Running ||
            race.Status.Phase != (operation == AdministrationOperation.OpenBetting ? RacePhase.Scheduled : RacePhase.BettingOpen))
            throw new InvalidOperationException("Race phase changed.");
        if (operation == AdministrationOperation.OpenBetting && Overview.Races.Races.Count(r => r.Status.Phase == RacePhase.BettingOpen) >= day.Definition.MaximumOpenRaces)
            throw new InvalidOperationException("Close an open race first.");
        return ReviewAdministration(operation, new RaceCommand { RaceDayId = day.Definition.RaceDayId, RaceId = raceId,
            ExpectedDayVersion = day.Status.Version, ExpectedRaceVersion = race.Status.Version, IdempotencyKey = Guid.NewGuid().ToString() },
            AdministrationContext(day.Definition.RaceDayId));
    }
    public static bool TryFunding(string text, out long value)
    {
        value = 0;
        return text.Length is > 0 and <= 19 && text.All(c => c is >= '0' and <= '9') &&
            long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
    public AdministrationReview ReviewEnrollment(string player, long paid, long promotional, bool additional)
    {
        var day = Overview?.Day ?? throw new InvalidOperationException("Current day required.");
        if (day.Status.Phase is not (DayPhase.Draft or DayPhase.Ready) || paid < 0 || promotional < 0 ||
            (additional && paid == 0 && promotional == 0) || !Guid.TryParseExact(player, "D", out var playerId))
            throw new InvalidOperationException("Check player, whole funding units and pre-start phase.");
        player = playerId.ToString("D");
        checked { _ = paid + promotional; }
        IMessage request;
        if (additional)
        {
            if (enrollment?.PlayerId != player || account?.PlayerId != player || account.AcceptedStakes != 0 || account.CashedOut)
                throw new InvalidOperationException("Load an enrolled account without wagers.");
            request = new AddOpeningFundingRequest { RaceDayId = day.Definition.RaceDayId, PlayerId = player, AdditionalPaid = paid,
                AdditionalPromotional = promotional, ExpectedAccountVersion = account.AccountVersion, IdempotencyKey = Guid.NewGuid().ToString() };
        }
        else request = new EnrollPlayerRequest { RaceDayId = day.Definition.RaceDayId, PlayerId = player, OpeningPaid = paid,
            OpeningPromotional = promotional, ExpectedDayVersion = day.Status.Version, IdempotencyKey = Guid.NewGuid().ToString() };
        return ReviewAdministration(additional ? AdministrationOperation.AddOpeningFunding : AdministrationOperation.EnrollPlayer,
            request, AdministrationContext(day.Definition.RaceDayId));
    }
    private AdministrationReview ReviewAdministration(AdministrationOperation operation, IMessage request, AdministrationBinding binding)
    {
        if (mutationBusy || Busy || administrationReview is not null) throw new InvalidOperationException("Finish the current review first.");
        var service = operation switch {
            AdministrationOperation.CashOut => "AccountService/",
            AdministrationOperation.CreateRaceDay or AdministrationOperation.PublishRaceDay or AdministrationOperation.StartRaceDay or AdministrationOperation.FinalizeRaceDay or AdministrationOperation.CompleteRaceDay or AdministrationOperation.CloseRaceDay => "RaceDayService/",
            AdministrationOperation.OpenBetting or AdministrationOperation.CloseBetting or AdministrationOperation.ScratchHorse or AdministrationOperation.VoidRace => "RaceService/",
            AdministrationOperation.SubmitResult or AdministrationOperation.VerifyResult or AdministrationOperation.SettleRace => "ResultService/", _ => "EnrollmentService/" };
        var record = new AdministrationRecord(1, Guid.NewGuid(), binding, AdministrationWire.Prefix + service + operation,
            request.ToByteArray(), AdministrationWire.Identity(AdministrationWire.Prefix + service + operation, request.ToByteArray()).Key,
            DateTimeOffset.UtcNow, MutationState.Prepared, null);
        AdministrationRights(record); EnsureAdministrationStorage();
        var prior = administrationJournal!.Read(binding);
        if (prior.Any(r => !r.Rejected && r.State != MutationState.Acked))
            throw new InvalidOperationException("Recover the saved command before starting another.");
        if (operation == AdministrationOperation.CreateRaceDay && prior.Any(r => r.Method == record.Method && !r.Rejected))
            throw new InvalidOperationException("This day already has a saved create command.");
        administrationReview = (generation, eventRevision, record);
        return new(record.Id, operation.ToString(), JsonFormatter.Default.Format(request));
    }
    public bool IsAdministrationReviewCurrent(Guid id)
    {
        if (administrationReview is not { } value || value.Record.Id != id || value.Actor != generation || value.Context != eventRevision) return false;
        try { AdministrationRights(value.Record); ValidateAdministrationVersion(value.Record); return true; } catch { return false; }
    }
    public void CancelAdministrationReview() => administrationReview = null;
    public async Task ConfirmAdministrationAsync(Guid id)
    {
        if (administrationReview is not { } value || value.Record.Id != id || value.Actor != generation || value.Context != eventRevision || mutationBusy)
            throw new InvalidOperationException("Review expired.");
        AdministrationRights(value.Record);
        ValidateAdministrationVersion(value.Record);
        administrationReview = null;
        administrationJournal!.Prepare(value.Record);
        await SendAdministrationAsync(value.Record);
    }
    private void ValidateAdministrationVersion(AdministrationRecord r)
    {
        if (r.Method.EndsWith("/CreateRaceDay", StringComparison.Ordinal))
        { if (UnusedEvent?.ToString() != r.Binding.Event) throw new InvalidOperationException("Event slot changed."); return; }
        var day = Overview?.Day ?? throw new InvalidOperationException("Current day required.");
        if (day.Definition.RaceDayId != r.Binding.Event) throw new InvalidOperationException("Event changed.");
        if (r.Method == AdministrationWire.Prefix + "AccountService/CashOut") {
            var cashout = CashOutRequest.Parser.ParseFrom(r.Request);
            if (selectedPlayer != cashout.PlayerId || account?.PlayerId != cashout.PlayerId || account.CashedOut || account.AccountVersion != cashout.ExpectedAccountVersion || day.Status.Version != cashout.ExpectedDayVersion || day.Status.Phase != DayPhase.Completed)
                throw new InvalidOperationException("Account or day changed — review again.");
            return;
        }
        if (Phase6Wire.IsMutation(r.Method))
        {
            var command = Phase6Wire.Command(Phase6Wire.Parse(r.Method, r.Request));
            var race = Overview!.Races.Races.Single(x => x.Definition.RaceId == command.RaceId);
            if (day.Status.Version != command.ExpectedDayVersion || race.Status.Version != command.ExpectedRaceVersion)
                throw new InvalidOperationException("Race changed — review again.");
            if (r.Method.EndsWith("/SettleRace", StringComparison.Ordinal) && race.Status.SelectedResultRevision != settlementReviewRevision)
                throw new InvalidOperationException("Result revision changed — review again.");
            return;
        }
        if (r.Method.Contains("RaceDayService/", StringComparison.Ordinal))
        {
            var command = DayCommand.Parser.ParseFrom(r.Request);
            if (day.Status.Version != command.ExpectedVersion) throw new InvalidOperationException("Day changed — review again.");
        }
        else if (r.Method.Contains("RaceService/", StringComparison.Ordinal))
        {
            var command = RaceCommand.Parser.ParseFrom(r.Request);
            if (day.Status.Version != command.ExpectedDayVersion ||
                Overview!.Races.Races.Single(x => x.Definition.RaceId == command.RaceId).Status.Version != command.ExpectedRaceVersion)
                throw new InvalidOperationException("Race changed — review again.");
        }
        else if (r.Method.EndsWith("/EnrollPlayer", StringComparison.Ordinal))
        {
            if (day.Status.Version != EnrollPlayerRequest.Parser.ParseFrom(r.Request).ExpectedDayVersion)
                throw new InvalidOperationException("Day changed — review again.");
        }
        else
        {
            var request = AddOpeningFundingRequest.Parser.ParseFrom(r.Request);
            if (account?.PlayerId != request.PlayerId || account.AccountVersion != request.ExpectedAccountVersion ||
                day.Status.Phase is not (DayPhase.Draft or DayPhase.Ready))
                throw new InvalidOperationException("Account changed — review again.");
        }
    }
    private async Task SendAdministrationAsync(AdministrationRecord record)
    {
        AdministrationRights(record);
        if (mutationBusy) throw new InvalidOperationException("Command already sending.");
        backend ??= backendFactory(Profile!);
        if (backend is not IAdministrationBackend transport) throw new InvalidOperationException("Administration transport unavailable.");
        var actor = generation; var context = eventRevision; var credential = material!.AccessToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, eventLifetime.Token);
        mutationBusy = true; AdministrationStatus = "Sending…"; Changed?.Invoke();
        try
        {
            var acknowledged = await new JournaledAdministration(administrationJournal!).SendAsync(record.Id, record.Binding,
                (saved, ct) => transport.TransmitAdministrationAsync(saved, credential, ct), cancellation.Token);
            if (actor == generation && context == eventRevision) {
                AdministrationStatus = "Completed"; UnusedEvent = null; playerReadDenied = false;
                if (record.Method is AdministrationWire.Prefix + "RaceService/ScratchHorse" or AdministrationWire.Prefix + "RaceService/VoidRace") {
                    var refund = RefundAcknowledgement.Parser.ParseFrom(acknowledged.Response!);
                    AdministrationStatus = $"Refunded {refund.RefundedLegs} legs · {refund.RefundedStake} {Overview?.Day.Definition.Currency}\n" +
                        string.Join("\n", refund.Players.Select(p => p.PlayerId + " · " + p.Amount));
                }
                if (record.Method == AdministrationWire.Prefix + "ResultService/SettleRace")
                    AdministrationStatus = "Settled · " + SettlementInformation.Parser.ParseFrom(acknowledged.Response!).SettlementId;
            }
        }
        catch (MutationUncertainException error)
        {
            if (actor == generation && context == eventRevision)
            {
                var saved = administrationJournal!.Read(record.Binding).Single(x => x.Id == record.Id);
                AdministrationStatus = saved.Rejected ? (error.Code == StatusCode.Aborted ? "Version changed — review a new action." :
                    error.Code == StatusCode.FailedPrecondition ? record.Method.Split('/').Last() switch {
                        "FinalizeRaceDay" => "Close all open betting before finalizing.",
                        "CompleteRaceDay" => "Settle or void every race and resolve all active legs before completing.",
                        "CloseRaceDay" => "Read the authorized full player list; record cashout for every enrolled account before closing.",
                        "CashOut" => "Cashout requires a completed day and an open account. Refresh the account and receipt.",
                        _ => "Server preconditions blocked this action." } : "Command rejected.") : "Uncertain — recover";
                if (error.Code == StatusCode.Unauthenticated) { Clear(); State = ConnectionState.Expired; }
                else if (error.Code == StatusCode.PermissionDenied) { Overview = null; enrollment = null; account = null; players = null; State = ConnectionState.Denied; }
            }
        }
        finally
        {
            mutationBusy = false;
            if (disposed) { administrationJournal?.Dispose(); administrationJournal = null; journal?.Dispose(); journal = null; }
            if (actor == generation && context == eventRevision) { Changed?.Invoke(); RefreshRequested?.Invoke(); }
        }
    }
    public IReadOnlyList<LocalAdministration> AdministrationHistory(Guid day)
    {
        var binding = AdministrationContext(day.ToString()); EnsureAdministrationStorage();
        return administrationJournal!.Read(binding).Select(r => {
            bool authorized;
            try { AdministrationRights(r); authorized = true; } catch { authorized = false; }
            return (r, authorized);
        }).Where(x => x.authorized).OrderByDescending(x => x.r.CreatedAt)
            .Select(x => new LocalAdministration(x.r.Id, x.r.Method.Split('/').Last(),
                x.r.Rejected ? "Rejected" : x.r.State.ToString(), !x.r.Rejected && x.r.State is MutationState.Prepared or MutationState.Unknown)).ToArray();
    }
    public async Task RecoverAdministrationAsync(Guid id, Guid day)
    {
        var binding = AdministrationContext(day.ToString()); EnsureAdministrationStorage();
        var record = administrationJournal!.Read(binding).Single(r => r.Id == id && !r.Rejected && r.State is MutationState.Prepared or MutationState.Unknown);
        await SendAdministrationAsync(record); // Deliberately skip current-version checks: replay the original request, never a replacement.
    }
    public async Task<bool> ReadSelectedPlayerAsync(CancellationToken cancellation)
    {
        if (selectedPlayer is not { } player || selectedEvent is not { } day || playerReadDenied || material is null) return true;
        var binding = AdministrationContext(day.ToString()); var grants = material.AdministrationGrants!;
        if (!(grants.Roles.Contains("AUDITOR") || (grants.Roles.Contains("CASHIER") && grants.Players.Contains(player)) || (grants.Roles.Contains("PLAYER") && grants.Player == player)))
        { enrollment = null; account = null; playerReadDenied = true; AccountReadStatus = "Access denied"; return true; }
        backend ??= backendFactory(Profile!);
        if (backend is not IAdministrationBackend admin || backend is not IAccountBackend accounts) return false;
        var actor = generation; var context = eventRevision;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, eventLifetime.Token, accountLifetime.Token);
        try
        {
            var nextEnrollment = await admin.GetEnrollmentAsync(new() { RaceDayId = binding.Event, PlayerId = player }, material.AccessToken, linked.Token);
            var nextAccount = await accounts.GetAccountAsync(new() { RaceDayId = binding.Event, PlayerId = player }, material.AccessToken, linked.Token);
            CheckExpiry();
            if (actor != generation || context != eventRevision || selectedPlayer != player || linked.IsCancellationRequested) return false;
            if (account is not null && account.AccountVersion != nextAccount.AccountVersion)
            {
                review = null;
                if (administrationReview?.Record.Method.EndsWith("/AddOpeningFunding", StringComparison.Ordinal) == true) administrationReview = null;
            }
            if (account is null && administrationReview?.Record.Method.EndsWith("/EnrollPlayer", StringComparison.Ordinal) == true) administrationReview = null;
            if (nextAccount.RaceDayId != binding.Event || nextAccount.PlayerId != player) throw new IOException("Account identity mismatch.");
            enrollment = nextEnrollment.Clone(); account = nextAccount.Clone(); AccountReadAt = DateTimeOffset.Now; AccountReadStatus = ""; Changed?.Invoke(); return true;
        }
        catch (RpcException error)
        {
            if (actor != generation || context != eventRevision || selectedPlayer != player || linked.IsCancellationRequested) return false;
            if (error.StatusCode == StatusCode.Unauthenticated) { Clear(); State = ConnectionState.Expired; }
            else if (error.StatusCode is StatusCode.PermissionDenied or StatusCode.NotFound)
            { enrollment = null; account = null; playerReadDenied = true; AccountReadStatus = error.StatusCode == StatusCode.PermissionDenied ? "Access denied" : "Not enrolled"; }
            else AccountReadStatus = "Offline";
            ResetAccounts();
            Changed?.Invoke();
            return error.StatusCode is StatusCode.PermissionDenied or StatusCode.NotFound;
        }
        catch (OperationCanceledException) { return false; }
    }
    public async Task ReadPlayersAsync()
    {
        if (!IsAuditor || selectedEvent is not { } day) throw new InvalidOperationException("Auditor access required.");
        var binding = AdministrationContext(day.ToString()); var actor = generation; var context = eventRevision;
        backend ??= backendFactory(Profile!);
        if (backend is not IAdministrationBackend transport) throw new InvalidOperationException();
        try
        {
            var result = await transport.ListPlayersAsync(new() { RaceDayId = binding.Event }, material!.AccessToken, eventLifetime.Token);
            CheckExpiry();
            if (actor != generation || context != eventRevision) return;
            var summaries = new List<string>();
            if (backend is IAccountBackend accounts && result.Players.Count <= 1000) {
                foreach (var player in result.Players) {
                    var a = await accounts.GetAccountAsync(new() { RaceDayId=binding.Event, PlayerId=player.PlayerId },material!.AccessToken,eventLifetime.Token);
                    if (a.RaceDayId!=binding.Event || a.PlayerId!=player.PlayerId) throw new IOException("Player account identity mismatch.");
                    summaries.Add($"{a.PlayerId} · {a.Balance} {a.Currency} · v{a.AccountVersion} · {(a.CashedOut ? "Cashed out" : "Open")}");
                }
            }
            CheckExpiry();
            if(actor!=generation || context!=eventRevision) return;
            PlayerAccountSummary = summaries.Count==result.Players.Count ? $"{summaries.Count} enrolled accounts\n" + string.Join("\n",summaries) : "Account summary unavailable; full player list loaded.";
            players = result.Clone(); Changed?.Invoke();
        }
        catch (RpcException error)
        {
            if (actor != generation || context != eventRevision) return;
            players = null;
            if (error.StatusCode == StatusCode.Unauthenticated) { Clear(); State = ConnectionState.Expired; }
            else AdministrationStatus = error.StatusCode == StatusCode.PermissionDenied ? "Access denied" : "Player list unavailable";
            Changed?.Invoke(); throw;
        }
    }
}
