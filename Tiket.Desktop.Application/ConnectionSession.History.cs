namespace Tiket.Desktop.Application;
public sealed partial class ConnectionSession {
    public string StaffHistoryText { get; private set; } = "";
    public async Task ReadStaffHistoryAsync(bool coupons) {
        var day=Overview?.Day.Definition.RaceDayId ?? throw new InvalidOperationException("Load an event.");
        var binding=AdministrationContext(day); var actor=generation; var context=eventRevision;
        var player=selectedPlayer; var race=resultRace;
        if(backend is not IStaffHistoryBackend transport) throw new InvalidOperationException("History unavailable.");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,eventLifetime.Token,accountLifetime.Token);
        try {
            var text=coupons ? await transport.PlayerCouponsAsync(day,player ?? throw new InvalidOperationException("Select a player."),material!.AccessToken,linked.Token) :
                await transport.RaceHistoryAsync(day,race ?? throw new InvalidOperationException("Select a race."),material!.AccessToken,linked.Token);
            CheckExpiry();
            if(actor==generation && context==eventRevision && player==selectedPlayer && race==resultRace && !linked.IsCancellationRequested) StaffHistoryText=text.Length==0 ? "No history" : text;
        } catch(Grpc.Core.RpcException e) {
            if(actor==generation && context==eventRevision) StaffHistoryText=e.StatusCode==Grpc.Core.StatusCode.PermissionDenied ? "History access denied" : "History unavailable";
        }
    }
}