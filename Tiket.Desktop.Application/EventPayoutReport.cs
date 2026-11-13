using System.Globalization;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public sealed record EventRacePayout(RaceInformation Race, OfficialResult? Result, WinnersReport? Winners);
public sealed record EventPlayerPayout(string Alias, AccountInformation Account, WithdrawalQuoteInformation? Quote, CashoutReceipt? Receipt);
public sealed record EventPayoutReport(string Event, string Currency, DateTimeOffset ReadAt, bool Complete, string Status,
    IReadOnlyList<EventRacePayout> Races, IReadOnlyList<EventPlayerPayout> Players)
{
    internal static EventPayoutReport Build(EventOverview overview, IReadOnlyList<EventRacePayout> races, IReadOnlyList<EventPlayerPayout> players)
    {
        var day = overview.Day.Definition.RaceDayId; var currency = overview.Day.Definition.Currency;
        var complete = overview.Day.Status.Phase is DayPhase.Completed or DayPhase.Closed && races.Count == overview.Races.Races.Count &&
            races.Select(r => r.Race.Definition.RaceId).Distinct().Count() == races.Count && players.Select(p => p.Account.PlayerId).Distinct().Count() == players.Count;
        foreach (var race in races)
        {
            complete &= race.Race.Status.RaceDayId == day && race.Race.Status.Phase == RacePhase.Settled && race.Result is not null && race.Winners?.Complete == true;
            complete &= race.Winners?.Settlement?.Pools.Select(p => p.Type).Order().SequenceEqual(new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }) == true;
        }
        foreach (var player in players)
        {
            var a = player.Account; var receipt = player.Receipt;
            complete &= a.RaceDayId == day && a.Currency == currency && (!a.CashedOut || receipt is not null);
            var balance = checked(a.OpeningPaid + a.OpeningPromotional - a.AcceptedStakes + a.RefundedStakes + a.CreditedWinnings -
                (receipt?.Withdrawn ?? 0) - (receipt?.PromotionalForfeiture ?? 0));
            complete &= balance == a.Balance;
            complete &= a.CreditedWinnings == races.Sum(r => r.Winners?.Rows.Where(w => w.Player == a.PlayerId).Sum(w => w.Award) ?? 0);
        }
        complete &= players.Sum(p => p.Account.CreditedWinnings) == races.Sum(r => r.Winners?.Settlement?.Pools.Sum(p => p.Distributed) ?? 0);
        complete &= checked(players.Sum(p => p.Account.AcceptedStakes) - players.Sum(p => p.Account.RefundedStakes)) ==
            races.Sum(r => r.Winners?.Settlement?.Pools.Sum(p => p.ValidStake) ?? 0);
        return new(day, currency, DateTimeOffset.Now, complete, complete ? "Complete · server settlements and accounts reconcile" : "Partial · missing data or totals do not reconcile", races, players);
    }
    public IReadOnlyList<string[]> Rows()
    {
        var rows = new List<string[]> { new[] { "Kind", "Race", "Alias", "Player UUID", "Coupon", "Leg", "Details", "Stake", "Award", "Opening paid", "Opening promotional", "Accepted stakes", "Refunded stakes", "Credited winnings", "Current balance", "Withdrawal", "Forfeiture", "Currency" } };
        string N(long n) => n.ToString(CultureInfo.InvariantCulture);
        foreach (var race in Races)
        {
            var number = race.Race.Definition.Number.ToString(CultureInfo.InvariantCulture);
            var finishers = race.Result is null ? "Official result unavailable" : string.Join(" → ", race.Result.FinisherIds.Select(id => race.Race.Definition.Runners.FirstOrDefault(h => h.HorseId == id) is { } h ? $"{h.ProgramNumber} {h.DisplayName} [{id}]" : id));
            rows.Add(["Result", number, "", "", "", "", finishers, "", "", "", "", "", "", "", "", "", "", Currency]);
            foreach (var pool in race.Winners?.Settlement?.Pools ?? [])
                rows.Add(["Pool", number, "", "", "", "", $"{pool.Type} · winning units {pool.WinningUnits} · distributable {pool.Distributable} · dividend/unit {pool.DividendPerUnit} · house {pool.HouseAllocation}", N(pool.ValidStake), N(pool.Distributed), "", "", "", "", "", "", "", "", Currency]);
            foreach (var win in race.Winners?.Rows ?? [])
                rows.Add(["Winning leg", number, Players.FirstOrDefault(p => p.Account.PlayerId == win.Player)?.Alias ?? "", win.Player, win.Coupon, win.Ordinal.ToString(CultureInfo.InvariantCulture),
                    $"{win.Type} · {win.Combination} · whole-coupon stake {win.CouponStake}", N(win.LegStake), N(win.Award), "", "", "", "", "", "", "", "", Currency]);
        }
        foreach (var player in Players)
        {
            var a = player.Account;
            rows.Add(["Player", "", player.Alias, a.PlayerId, "", "", player.Receipt?.ReceiptReference ?? (player.Quote is null ? "Withdrawal quote unavailable" : $"Withdrawal quote {player.Quote.Withdrawable}; forfeitable {player.Quote.Forfeitable}"),
                "", "", N(a.OpeningPaid), N(a.OpeningPromotional), N(a.AcceptedStakes), N(a.RefundedStakes), N(a.CreditedWinnings), N(a.Balance), N(player.Receipt?.Withdrawn ?? 0), N(player.Receipt?.PromotionalForfeiture ?? 0), Currency]);
        }
        rows.Add(["Total", "", "", "", "", "", Status, "", "", N(Players.Sum(p => p.Account.OpeningPaid)), N(Players.Sum(p => p.Account.OpeningPromotional)),
            N(Players.Sum(p => p.Account.AcceptedStakes)), N(Players.Sum(p => p.Account.RefundedStakes)), N(Players.Sum(p => p.Account.CreditedWinnings)), N(Players.Sum(p => p.Account.Balance)),
            N(Players.Sum(p => p.Receipt?.Withdrawn ?? 0)), N(Players.Sum(p => p.Receipt?.PromotionalForfeiture ?? 0)), Currency]);
        return rows;
    }
    public string Display()
    {
        var rows = Rows(); var headers = rows[0];
        return $"Event {Event} · {ReadAt:g}\n{Status}\n\n" + string.Join("\n\n", rows.Skip(1).Select(row =>
            string.Join(" · ", row.Select((value, i) => value.Length == 0 ? "" : headers[i] + ": " + value).Where(s => s.Length > 0))));
    }
    public string Csv()
    {
        static string Cell(string value) { if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        return string.Join("\r\n", Rows().Select(row => string.Join(",", row.Select(Cell)))) + "\r\n";
    }
}
