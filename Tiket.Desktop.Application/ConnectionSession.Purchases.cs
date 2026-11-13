using System.Security.AccessControl;
using System.Security.Principal;
using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public sealed record PurchaseReview(Guid ReviewId, string Summary);
public sealed record LocalPurchase(Guid Id, string Coupon, string State, string Summary, bool CanRecover, bool CanReprint);

public sealed partial class ConnectionSession
{
    private MutationJournal? journal;
    private (long Generation, MutationRecord Record)? review;
    private bool mutationBusy;
    private readonly HashSet<Guid> inspectedPrintJobs = [];
    internal ConnectionSession(INativeLogin login, Func<ConnectionProfile, IReadOnlyBackend> backendFactory, MutationJournal journal)
        : this(login, backendFactory) => this.journal = journal;
    public string PurchaseStatus { get; private set; } = "No live purchase submitted.";
    public string? CashierIdentifier => material?.OperatingGrants?.Cashier;
    public IReadOnlyList<int> PointOfSales => material?.OperatingGrants?.Poses.ToArray() ?? [];
    public bool MutationBusy => mutationBusy;

    public async Task<string> ReadPurchaseAccountAsync(string player, int pos)
    {
        var quote = Composer.Response ?? throw new InvalidOperationException("Quote required.");
        Binding(quote.Pricing.RaceDayId, pos);
        if (material?.OperatingGrants?.Players.Contains(player) != true) throw new InvalidOperationException("Player unavailable.");
        var current = generation;
        backend ??= backendFactory(Profile!);
        if (backend is not ReadOnlyBackend transport) throw new InvalidOperationException();
        var account = await transport.ReadAccountAsync(new AccountQuery { RaceDayId = quote.Pricing.RaceDayId, PlayerId = player },
            material.AccessToken, lifetime.Token);
        if (current != generation) throw new OperationCanceledException();
        return $"Authorized balance before purchase: {account.Balance} {account.Currency} · account version {account.AccountVersion}";
    }

    private MutationBinding Binding(string day, int pos)
    {
        CheckExpiry();
        var grants = material?.OperatingGrants;
        if (disposed || Profile is null || grants is null || grants.Subject != Subject ||
            !grants.Events.Contains(day) || !grants.Poses.Contains(pos))
            throw new InvalidOperationException("Verified subject/event/POS operating context unavailable.");
        return new(Profile.Id, Profile.Backend.OriginalString, Profile.Issuer.OriginalString, Profile.ClientId,
            grants.Subject, day, pos);
    }

    private void EnsureJournal()
    {
        if (journal is not null) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tiket Desktop", "PurchaseJournal", "v1");
        if (new DriveInfo(Path.GetPathRoot(directory)!).DriveType != DriveType.Fixed)
            throw new IOException("Journal requires local fixed storage.");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows identity unavailable.");
        var info = new DirectoryInfo(directory);
        if (!info.Exists)
        {
            info.Create();
            var acl = new DirectorySecurity();
            acl.SetOwner(owner);
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        var current = info.GetAccessControl();
        if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("Journal owner mismatch.");
        foreach (FileSystemAccessRule rule in current.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(owner) &&
                rule.IdentityReference.Value is not ("S-1-5-18" or "S-1-5-32-544"))
                throw new IOException("Journal ACL requires administrator review.");
        journal = new MutationJournal(directory);
    }

    public PurchaseReview ReviewPurchase(string player, int pos)
    {
        if (review is not null || mutationBusy || Busy || !Guid.TryParse(player, out var playerId) ||
            material?.OperatingGrants is not { } grants || !grants.Players.Contains(playerId.ToString()))
            throw new InvalidOperationException("Select a player authorized by the verified token.");
        var quote = Composer.Response ?? throw new InvalidOperationException("Obtain a fresh authoritative quote.");
        var p = quote.Pricing ?? throw new InvalidOperationException("Quote pricing unavailable.");
        if (p.Selection is null || p.UnitsPerCombination <= 0 || p.TotalStake <= 0 || string.IsNullOrWhiteSpace(p.Currency) ||
            string.IsNullOrWhiteSpace(quote.Confirmation) || quote.RaceDayVersion < 0 || quote.RaceVersion < 0)
            throw new InvalidOperationException("Server quote is incomplete; refresh and review again.");
        var binding = Binding(p.RaceDayId, pos);
        EnsureJournal(); // No review that could be confirmed without protected local storage readiness.
        if (journal!.Read(binding).Any(r => r.State is MutationState.Unknown or MutationState.Sending or MutationState.Prepared))
            throw new InvalidOperationException("Resolve the existing intent before a new sale in this context.");
        var request = new PurchaseCouponRequest { RaceDayId = p.RaceDayId, RaceId = p.RaceId,
            PlayerId = playerId.ToString(), CouponId = Guid.NewGuid().ToString(), BetType = p.BetType,
            Selection = p.Selection.Clone(), UnitsPerCombination = p.UnitsPerCombination,
            QuoteConfirmation = quote.Confirmation, ExpectedDayVersion = quote.RaceDayVersion,
            ExpectedRaceVersion = quote.RaceVersion, PointOfSaleNumber = pos, CashierIdentifier = grants.Cashier,
            IdempotencyKey = Guid.NewGuid().ToString() };
        var record = new MutationRecord(1, Guid.NewGuid(), binding,
            "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon", request.ToByteArray(), request.CouponId,
            request.IdempotencyKey, request.PlayerId, request.CashierIdentifier, quote.ToByteArray(),
            DateTimeOffset.UtcNow, MutationState.Prepared, null, null);
        review = (generation, record);
        return new(record.Id, $"Player: {playerId}\nPOS: {pos} · Cashier: {grants.Cashier}\n{p.BetType}\n" +
            $"Selection: {CashierComposer.Canonical(p.Selection, Overview?.Races.Races.FirstOrDefault(x => x.Definition.RaceId == p.RaceId)?.Definition.Runners ?? new Google.Protobuf.Collections.RepeatedField<Runner>())}\nTotal: {p.TotalStake} {p.Currency}");
    }

    public void CancelPurchaseReview() => review = null;

    public async Task ConfirmPurchaseAsync(Guid reviewId)
    {
        if (review is not { } value || value.Record.Id != reviewId || value.Generation != generation || mutationBusy)
            throw new InvalidOperationException("Review expired. Obtain and review current terms.");
        var r = value.Record;
        if (Binding(r.Binding.Event, r.Binding.Pos) != r.Binding || Composer.Response is not { } quote ||
            !quote.ToByteArray().AsSpan().SequenceEqual(r.ReviewedQuote))
            throw new InvalidOperationException("Quote or identity changed. Review again.");
        review = null;
        PurchaseStatus = "PREPARING · encrypting and committing the reviewed original intent before dispatch.";
        Changed?.Invoke();
        try { journal!.Prepare(r); }
        catch
        {
            PurchaseStatus = "PREPARATION FAILED · no purchase RPC dispatched. Preserve the journal and investigate storage/encryption.";
            Changed?.Invoke();
            throw;
        }
        await SubmitAsync(r);
    }

    private async Task SubmitAsync(MutationRecord r)
    {
        var current = generation;
        var token = material?.AccessToken ?? throw new InvalidOperationException("Sign in as the original operator.");
        if (Binding(r.Binding.Event, r.Binding.Pos) != r.Binding || mutationBusy) throw new InvalidOperationException("Context unavailable.");
        RequireRecordRights(r);
        backend ??= backendFactory(Profile!);
        if (backend is not ReadOnlyBackend transport) throw new InvalidOperationException("Mutation transport unavailable.");
        mutationBusy = true;
        PurchaseStatus = "SENDING · exact intent saved. Do not start a replacement sale.";
        Changed?.Invoke();
        try
        {
            await new JournaledMutation(journal!).SendAsync(r.Id, r.Binding,
                (saved, ct) => transport.TransmitJournaledAsync(saved, token, ct), lifetime.Token);
            if (current == generation) PurchaseStatus = "ACKED · original server response durably saved. Inspect current print status separately.";
        }
        catch (MutationUncertainException error)
        {
            if (current == generation)
            {
                if (error.Code == Grpc.Core.StatusCode.Unauthenticated)
                {
                    Clear(); State = ConnectionState.Expired;
                    PurchaseStatus = "UNKNOWN · possible debit. Reauthenticate the original identity for exact-intent recovery.";
                    Changed?.Invoke();
                }
                else PurchaseStatus = "UNKNOWN · possible debit. Recover the original intent; never replace its key. " +
                    (error.Code is { } code ? MutationMessages.For(code) : "Transport or durable acknowledgement failed; investigate the retained journal.");
            }
        }
        finally
        {
            mutationBusy = false;
            if (disposed) { journal?.Dispose(); journal = null; }
            if (current == generation) Changed?.Invoke();
        }
    }

    public IReadOnlyList<LocalPurchase> LocalPurchases(string day, int pos)
    {
        var binding = Binding(day, pos);
        EnsureJournal();
        var records = journal!.Read(binding);
        var unresolved = records.Where(r => r.State is MutationState.Unknown or MutationState.Sending or MutationState.Prepared)
            .OrderBy(r => r.State == MutationState.Unknown ? 0 : r.State == MutationState.Sending ? 1 : 2).FirstOrDefault();
        if (unresolved is not null)
            PurchaseStatus = !HasRecordRights(unresolved)
                ? "Stored unresolved intent requires restored original grants. Details are hidden; preserve the journal and escalate."
                : $"{unresolved.State.ToString().ToUpperInvariant()} · original saved intent needs attention. Recovery may complete the original debit; never replace its key.";
        return records.Where(HasRecordRights).OrderBy(r => r.State is MutationState.Unknown or MutationState.Sending or MutationState.Prepared ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt).Select(r => new LocalPurchase(r.Id, r.Coupon,
            r.State.ToString().ToUpperInvariant(), SavedSummary(r), r.State is MutationState.Unknown or MutationState.Prepared,
            r.State == MutationState.Acked && r.OriginalPurchase is null)).ToArray();
    }

    private static string SavedSummary(MutationRecord r)
    {
        var summary = $"{(r.OriginalPurchase is null ? "PURCHASE / ORIGINAL" : "REPRINT")} · {r.State}\nCoupon: {r.Coupon}\nCreated: {r.CreatedAt:g}";
        if (r.Response is not null && r.OriginalPurchase is null)
        {
            var ack = PurchaseCouponResponse.Parser.ParseFrom(r.Response);
            summary += $"\nHISTORICAL ACK · {ack.Pricing.TotalStake} {ack.Pricing.Currency}\n" +
                $"Account version: {ack.AccountVersion}\nJob: {ack.PrintJobId} · initial {ack.PrintingStatus}";
        }
        else if (r.Response is not null)
        {
            var ack = PrintStatus.Parser.ParseFrom(r.Response);
            summary += $"\nHISTORICAL REPRINT ACK · Job: {ack.PrintJobId} · initial {ack.Status}";
        }
        return summary;
    }

    public async Task RecoverPurchaseAsync(Guid id, string day, int pos)
    {
        var binding = Binding(day, pos);
        EnsureJournal();
        var r = journal!.Read(binding).Single(x => x.Id == id);
        await SubmitAsync(r);
    }

    public async Task<string> RefreshPrintAsync(Guid id, string day, int pos, CancellationToken cancellation = default)
    {
        var binding = Binding(day, pos);
        EnsureJournal();
        var r = journal!.Read(binding).Single(x => x.Id == id && x.State == MutationState.Acked);
        RequireRecordRights(r);
        var job = r.OriginalPurchase is null ? PurchaseCouponResponse.Parser.ParseFrom(r.Response!).PrintJobId :
            PrintStatus.Parser.ParseFrom(r.Response!).PrintJobId;
        var current = generation;
        backend ??= backendFactory(Profile!);
        if (backend is not ReadOnlyBackend transport) throw new InvalidOperationException();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation);
        var status = await transport.ReadPrintStatusAsync(job, material!.AccessToken, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (current != generation || status.CouponId != r.Coupon || status.PrintJobId != job ||
            status.Type != (r.OriginalPurchase is null ? PrintJobType.Original : PrintJobType.Reprint)) throw new OperationCanceledException();
        if (r.OriginalPurchase is null) inspectedPrintJobs.Add(r.Id);
        var meaning = status.Status switch
        {
            DispatchStatus.Pending => "Pending print.",
            DispatchStatus.Claimed => "Printing.",
            DispatchStatus.Retrying => "Printer retry pending.",
            DispatchStatus.Dispatched => "Sent to printer.",
            DispatchStatus.PermanentlyFailed => "Backend attempt limit/final error; inspect output before explicit reprint.",
            _ => "Unrecognized backend status; investigate."
        };
        return $"{status.Type} · {status.Status} · {meaning}";
    }

    public async Task ReprintAsync(Guid id, string day, int pos)
    {
        if (mutationBusy) throw new InvalidOperationException("Mutation already in progress.");
        var binding = Binding(day, pos);
        EnsureJournal();
        var parent = journal!.Read(binding).Single(x => x.Id == id && x.State == MutationState.Acked && x.OriginalPurchase is null);
        RequireRecordRights(parent);
        if (!inspectedPrintJobs.Contains(id)) throw new InvalidOperationException("Inspect the original current print status before requesting reprint.");
        if (journal.Read(binding).Any(x => x.OriginalPurchase == id && x.State is MutationState.Prepared or MutationState.Unknown or MutationState.Sending))
            throw new InvalidOperationException("Recover the existing uncertain reprint before requesting another.");
        var request = new ReprintCouponRequest { RaceDayId = day, CouponId = parent.Coupon, IdempotencyKey = Guid.NewGuid().ToString() };
        var r = parent with { Id = Guid.NewGuid(), Method = "pl.umamusume.tiket.api.v1.PrintingService/ReprintCoupon",
            Request = request.ToByteArray(), Key = request.IdempotencyKey, State = MutationState.Prepared, Response = null,
            OriginalPurchase = id, CreatedAt = DateTimeOffset.UtcNow };
        journal.Prepare(r);
        await SubmitAsync(r);
    }

    private bool HasRecordRights(MutationRecord r) => material?.OperatingGrants is { } grants &&
        grants.Subject == r.Binding.Subject && grants.Cashier == r.Cashier && grants.Players.Contains(r.Player);
    private void RequireRecordRights(MutationRecord r)
    {
        if (!HasRecordRights(r)) throw new InvalidOperationException("Original player/cashier grants unavailable; preserve the intent and restore rights.");
    }
    public bool CanRequestReprint(Guid id) => inspectedPrintJobs.Contains(id) && !mutationBusy && material?.OperatingGrants is not null;

    public async Task MonitorPrintAsync(Guid id, string day, int pos, Action<string> update)
    {
        var current = generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        foreach (var seconds in new[] { 0, 2, 5 })
        {
            if (seconds > 0) await Task.Delay(TimeSpan.FromSeconds(seconds), linked.Token);
            if (current != generation) return;
            var status = await RefreshPrintAsync(id, day, pos);
            if (current != generation) return;
            update(status);
            if (status.Contains("· Dispatched ·", StringComparison.Ordinal) || status.Contains("· PermanentlyFailed ·", StringComparison.Ordinal)) return;
        }
    }
}
