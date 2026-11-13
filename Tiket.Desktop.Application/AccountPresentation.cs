using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;

public static class AccountPresentation
{
    public static string Account(AccountInformation a) => $"Opening paid {a.OpeningPaid} · promotional {a.OpeningPromotional}\nAccepted stakes {a.AcceptedStakes} · refunded {a.RefundedStakes}\nQualifying turnover {a.QualifyingTurnover} · winnings {a.CreditedWinnings}\nBalance {a.Balance} {a.Currency} · account v{a.AccountVersion} · {(a.CashedOut ? "Cashed out" : "Open")}";
    public static string Quote(WithdrawalQuoteInformation q) => $"Unlocked promotional {q.UnlockedPromotional}\nEligible recorded cashout {q.Withdrawable} {q.Account.Currency}\nPromotional forfeiture {q.Forfeitable} {q.Account.Currency}\nAccount v{q.Account.AccountVersion} · day v{q.RaceDayVersion}";
    public static string Receipt(CashoutReceipt r) => $"Recorded cashout {r.Withdrawn} {r.Currency} · promotional forfeiture {r.PromotionalForfeiture}\nEnding balance {r.Balance} · account v{r.AccountVersion}\n{r.CashedOutAt} · {r.ReceiptReference}";
    internal static void VerifyReceipt(AdministrationRecord record, byte[] bytes)
    {
        var request = CashOutRequest.Parser.ParseFrom(record.Request);
        var r = CashoutReceipt.Parser.ParseFrom(bytes);
        if (r.RaceDayId != request.RaceDayId || r.PlayerId != request.PlayerId || r.Balance != 0 ||
            r.Withdrawn < 0 || r.PromotionalForfeiture < 0 || r.AccountVersion != checked(request.ExpectedAccountVersion + 1) ||
            string.IsNullOrWhiteSpace(r.Currency) || string.IsNullOrWhiteSpace(r.ReceiptReference) || string.IsNullOrWhiteSpace(r.CashedOutAt))
            throw new IOException("Cashout acknowledgement mismatch.");
    }
    internal static async Task<IReadOnlyList<FinancialMovement>> Movements(
        Func<long, Task<AccountMovements>> read)
    {
        var rows = new List<FinancialMovement>(); long after = 0;
        for (var page = 0; page < 1000; page++) {
            var response = await read(after);
            foreach (var m in response.Movements) {
                if (m.Sequence <= after) throw new IOException("Movement sequence did not advance.");
                after = m.Sequence; rows.Add(m.Clone());
            }
            if (!response.HasNextSequence) return rows;
            if (response.Movements.Count == 0 || response.NextSequence != after)
                throw new IOException("Invalid movement continuation.");
        }
        throw new IOException("Movement history exceeds bounded read. Narrow the account query.");
    }
}
