using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;
internal interface IStaffHistoryBackend {
    Task<string> PlayerCouponsAsync(string day, string player, string token, CancellationToken ct);
    Task<string> RaceHistoryAsync(string day, string race, string token, CancellationToken ct);
}
public sealed partial class ReadOnlyBackend : IStaffHistoryBackend {
    async Task<string> IStaffHistoryBackend.PlayerCouponsAsync(string day,string player,string token,CancellationToken ct) {
        var client = new BettingService.BettingServiceClient(channel);
        var rows = new List<string>(); var offset = 0;
        for(var page=0;page<200;page++) {
            var result = await client.ListPlayerCouponsAsync(new() { RaceDayId=day,PlayerId=player,PageSize=100,Offset=offset },ResultHeaders(token),DateTime.UtcNow.Add(deadline),ct);
            foreach(var coupon in result.Coupons) {
                if(coupon.PlayerId!=player || coupon.Pricing?.RaceDayId!=day) throw new IOException("Coupon identity mismatch.");
                var detail = await client.GetCouponAsync(new() { RaceDayId=day,CouponId=coupon.CouponId },ResultHeaders(token),DateTime.UtcNow.Add(deadline),ct);
                if(detail.PlayerId!=player || detail.CouponId!=coupon.CouponId || detail.Pricing?.RaceDayId!=day) throw new IOException("Coupon detail mismatch.");
                rows.Add($"Coupon {detail.CouponId} · {detail.PurchasedAt}\nRace {detail.Pricing.RaceId} · {detail.Pricing.BetType} · stake {detail.Pricing.TotalStake} {detail.Pricing.Currency}\nPlayer {detail.PlayerId} · POS {detail.PointOfSaleNumber}\n" + string.Join("\n",detail.Legs.Select(l => $"Leg {l.Ordinal}: {string.Join(", ",l.HorseIds)} · units {l.Units} · stake {l.Stake}")));
            }
            if(!result.HasNextOffset) return string.Join("\n",rows);
            if(result.NextOffset<=offset) throw new IOException("Invalid coupon continuation.");
            offset=result.NextOffset;
        }
        throw new IOException("Coupon history exceeds bounded read.");
    }
    async Task<string> IStaffHistoryBackend.RaceHistoryAsync(string day,string race,string token,CancellationToken ct) {
        var query=new RaceQuery { RaceDayId=day,RaceId=race };
        var status=await new RaceService.RaceServiceClient(channel).GetRaceStatusAsync(query,ResultHeaders(token),DateTime.UtcNow.Add(deadline),ct);
        if(status.RaceDayId!=day || status.RaceId!=race) throw new IOException("Race status mismatch.");
        var rows=new List<string> { $"Race {status.RaceId} · {status.Phase} · v{status.Version} · selected revision {status.SelectedResultRevision}" }; var offset=0;
        for(var page=0;page<200;page++) {
            ResultRevisions result;
            try { result=await new ResultService.ResultServiceClient(channel).ListResultRevisionsAsync(new() { Race=query,PageSize=100,Offset=offset },ResultHeaders(token),DateTime.UtcNow.Add(deadline),ct); }
            catch(RpcException e) when(e.StatusCode==StatusCode.PermissionDenied) { return string.Join("\n",rows)+"\nResult revisions access denied"; }
            foreach(var revision in result.Results) {
                if(revision.RaceDayId!=day || revision.RaceId!=race) throw new IOException("Revision identity mismatch.");
                rows.Add($"Revision {revision.Revision} · {revision.Status} · {revision.Kind} · finishers {string.Join(", ",revision.FinisherIds)}");
            }
            if(!result.HasNextOffset) return string.Join("\n",rows);
            if(result.NextOffset<=offset) throw new IOException("Invalid revision continuation.");
            offset=result.NextOffset;
        }
        throw new IOException("Revision history exceeds bounded read.");
    }
}