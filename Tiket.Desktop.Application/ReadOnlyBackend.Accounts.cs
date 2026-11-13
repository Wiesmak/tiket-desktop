using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;
internal interface IAccountHistoryBackend
{
    Task<WithdrawalQuoteInformation> QuoteAsync(AccountQuery q, string token, CancellationToken ct);
    Task<CashoutReceipt> ReceiptAsync(AccountQuery q, string token, CancellationToken ct);
    Task<AccountMovements> MovementsAsync(AccountMovementsRequest q, string token, CancellationToken ct);
}
public sealed partial class ReadOnlyBackend : IAccountHistoryBackend
{
    async Task<WithdrawalQuoteInformation> IAccountHistoryBackend.QuoteAsync(AccountQuery q,string token,CancellationToken ct) =>
        await new AccountService.AccountServiceClient(channel).GetWithdrawalQuoteAsync(q, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
    async Task<CashoutReceipt> IAccountHistoryBackend.ReceiptAsync(AccountQuery q,string token,CancellationToken ct) =>
        await new AccountService.AccountServiceClient(channel).GetCashoutReceiptAsync(q, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
    async Task<AccountMovements> IAccountHistoryBackend.MovementsAsync(AccountMovementsRequest q,string token,CancellationToken ct) =>
        await new AccountService.AccountServiceClient(channel).ListAccountMovementsAsync(q, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
}