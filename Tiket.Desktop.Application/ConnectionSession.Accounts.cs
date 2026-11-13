using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;
public sealed partial class ConnectionSession
{
    private CancellationTokenSource accountLifetime = new();
    private long accountRead;
    private WithdrawalQuoteInformation? withdrawalQuote;
    public WithdrawalQuoteInformation? WithdrawalQuote => withdrawalQuote?.Clone();
    public string AccountHistoryText { get; private set; } = "";
    public string WithdrawalText { get; private set; } = "";
    public bool CanCashOut => IsEnrollmentCashier && withdrawalQuote is not null && !mutationBusy && !Busy &&
        withdrawalQuote.Account.PlayerId == selectedPlayer && material?.AdministrationGrants?.Players.Contains(selectedPlayer!) == true;
    private void ResetAccounts() {
        accountRead++; accountLifetime.Cancel(); accountLifetime.Dispose(); accountLifetime = new();
        StaffHistoryText = ""; withdrawalQuote = null; AccountHistoryText = WithdrawalText = "";
    }
    public AdministrationReview ReviewCashOut()
    {
        var q = withdrawalQuote ?? throw new InvalidOperationException("Load a completed-day quote.");
        if (!CanCashOut || q.Account.CashedOut || q.Account.Phase != DayPhase.Completed) throw new InvalidOperationException("Cashout unavailable.");
        var request = new CashOutRequest { RaceDayId = q.Account.RaceDayId, PlayerId = q.Account.PlayerId,
            ExpectedDayVersion = q.RaceDayVersion, ExpectedAccountVersion = q.Account.AccountVersion, IdempotencyKey = Guid.NewGuid().ToString() };
        var review = ReviewAdministration(AdministrationOperation.CashOut, request, AdministrationContext(request.RaceDayId));
        return review with { Title = "Record cashout", Payload = $"Player {request.PlayerId}\n{AccountPresentation.Quote(q)}\nLedger finalization only; no physical payment is verified." };
    }
    public async Task<bool> ReadAccountHistoryAsync(CancellationToken cancellation)
    {
        if (account is null || selectedPlayer is not { } player || material is null || mutationBusy) return true;
        var binding = AdministrationContext(account.RaceDayId);
        if (backend is not IAccountHistoryBackend transport) return false;
        var stamp = ++accountRead; var actor = generation; var context = eventRevision; var token = material.AccessToken;
        var snapshot = account.Clone();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, eventLifetime.Token, accountLifetime.Token);
        bool Current() => stamp == accountRead && actor == generation && context == eventRevision && selectedPlayer == player && !linked.IsCancellationRequested;
        try {
            var query = new AccountQuery { RaceDayId = binding.Event, PlayerId = player };
            var rows = await AccountPresentation.Movements(after => transport.MovementsAsync(new() { Account = query, PageSize = 100, AfterSequence = after }, token, linked.Token));
            WithdrawalQuoteInformation? quote = null; CashoutReceipt? receipt = null;
            if (snapshot.CashedOut) {
                receipt = await transport.ReceiptAsync(query, token, linked.Token);
                if (receipt.RaceDayId != binding.Event || receipt.PlayerId != player || receipt.Balance != 0)
                    throw new IOException("Receipt identity mismatch.");
            } else if (Overview?.Day.Status.Phase == DayPhase.Completed) {
                quote = await transport.QuoteAsync(query, token, linked.Token);
                if (quote.Account?.RaceDayId != binding.Event || quote.Account.PlayerId != player ||
                    quote.Account.AccountVersion != snapshot.AccountVersion || quote.RaceDayVersion != Overview.Day.Status.Version)
                    throw new IOException("Quote changed during read.");
            }
            CheckExpiry(); if (!Current()) return false;
            withdrawalQuote = quote;
            WithdrawalText = receipt is not null ? AccountPresentation.Receipt(receipt) : quote is not null ? AccountPresentation.Quote(quote) : "Withdrawal quote available after day COMPLETED.";
            AccountHistoryText = string.Join("\n", rows.Select(m => $"{m.Sequence} · {m.CreatedAt} · {m.Kind} · {m.Amount} · {m.MovementKey} · {m.CouponId} · {m.SettlementId}"));
            Changed?.Invoke(); return true;
        } catch (OperationCanceledException) { return false; }
        catch (Exception e) {
            if (!Current()) return false;
            withdrawalQuote = null; AccountHistoryText = ""; WithdrawalText = e is RpcException rpc && rpc.StatusCode == StatusCode.PermissionDenied ? "Account history access denied" : "Account history unavailable";
            Changed?.Invoke(); return false;
        }
    }
}
