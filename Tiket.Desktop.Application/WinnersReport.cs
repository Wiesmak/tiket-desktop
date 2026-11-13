using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;

public sealed record WinnerRow(string Coupon, string Player, PoolType Type, int Ordinal, string Combination, long LegStake, long CouponStake, long Award, string Currency);
public sealed record WinnersReport(IReadOnlyList<WinnerRow> Rows, SettlementInformation? Settlement, bool Complete, string Status)
{
    internal static WinnersReport Build(string day, string race, string currency, IReadOnlyList<TerminalLegResolution> resolutions,
        IReadOnlyDictionary<string, CouponInformation> coupons, SettlementInformation? settlement, bool complete)
    {
        var rows = new List<WinnerRow>();
        var seen = new HashSet<(string, int)>();
        foreach (var won in resolutions.Where(r => r.Outcome == TerminalLegOutcome.Won)) {
            if (!seen.Add((won.CouponId, won.Ordinal)) || won.Amount < 0) { complete = false; continue; }
            if (!coupons.TryGetValue(won.CouponId, out var coupon)) { complete = false; continue; }
            var leg = coupon.Legs.SingleOrDefault(l => l.Ordinal == won.Ordinal);
            if (coupon.CouponId != won.CouponId || coupon.Pricing?.RaceDayId != day || coupon.Pricing.RaceId != race ||
                coupon.Pricing.Currency != currency || leg is null || leg.Stake < 0 || coupon.Pricing.TotalStake < 0 ||
                (settlement is not null && won.SettlementId != settlement.SettlementId)) { complete = false; continue; }
            rows.Add(new(coupon.CouponId, coupon.PlayerId, coupon.Pricing.BetType, leg.Ordinal, string.Join(" → ", leg.HorseIds),
                leg.Stake, coupon.Pricing.TotalStake, won.Amount, currency));
        }
        if (settlement is null) complete = false;
        else {
            var paid = rows.Sum(r => r.Award);
            if (paid != settlement.Players.Sum(p => p.Amount) || paid != settlement.Pools.Sum(p => p.Distributed)) complete = false;
            foreach (var p in settlement.Players)
                if (rows.Where(r => r.Player == p.PlayerId).Sum(r => r.Award) != p.Amount) complete = false;
            foreach (var pool in settlement.Pools)
                if (rows.Where(r => r.Type == pool.Type).Sum(r => r.Award) != pool.Distributed) complete = false;
        }
        return new(rows.OrderBy(r => r.Player).ThenBy(r => r.Type).ThenBy(r => r.Coupon).ThenBy(r => r.Ordinal).ToArray(),
            settlement, complete, settlement is null ? "Settlement details unavailable" : complete ? "Complete · payouts reconcile" : "Partial data · payouts do not fully reconcile");
    }
}
