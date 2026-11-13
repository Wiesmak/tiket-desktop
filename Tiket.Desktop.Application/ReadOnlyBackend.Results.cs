using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
namespace Tiket.Desktop.Application;

internal interface IResultsBackend
{
    Task<ResultAcknowledgement> GetResultAsync(RaceQuery query, string token, CancellationToken ct);
    Task<RaceLegResolutions> ListResolutionsAsync(RaceLegResolutionsRequest query, string token, CancellationToken ct);
    Task<SettlementInformation> GetSettlementAsync(SettlementQuery query, string token, CancellationToken ct);
    Task<CouponInformation> GetWinningCouponAsync(CouponQuery query, string token, CancellationToken ct);
}
public sealed partial class ReadOnlyBackend : IResultsBackend
{
    private Metadata ResultHeaders(string token) => new() { { "authorization", "Bearer " + token } };
    async Task<ResultAcknowledgement> IResultsBackend.GetResultAsync(RaceQuery query, string token, CancellationToken ct) =>
        await new ResultService.ResultServiceClient(channel).GetResultAsync(query, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
    async Task<RaceLegResolutions> IResultsBackend.ListResolutionsAsync(RaceLegResolutionsRequest query, string token, CancellationToken ct) =>
        await new ResultService.ResultServiceClient(channel).ListRaceLegResolutionsAsync(query, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
    async Task<SettlementInformation> IResultsBackend.GetSettlementAsync(SettlementQuery query, string token, CancellationToken ct) =>
        await new ResultService.ResultServiceClient(channel).GetSettlementAsync(query, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
    async Task<CouponInformation> IResultsBackend.GetWinningCouponAsync(CouponQuery query, string token, CancellationToken ct) =>
        await new BettingService.BettingServiceClient(channel).GetCouponAsync(query, ResultHeaders(token), DateTime.UtcNow.Add(deadline), ct);
}
