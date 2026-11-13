using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Tiket.Desktop.Application;

public sealed partial class ConnectionSession
{
    public bool CanProvisionLab => Profile is { DevelopmentLoopback: true } p &&
        p.Backend.Host == "127.0.0.1" && p.Issuer.OriginalString.TrimEnd('/') == "https://localhost:8443/realms/tiket-lab" && IsEventAdmin;

    public async Task ProvisionLabAsync(Guid day, IReadOnlyList<Guid> players)
    {
        CheckExpiry();
        if (!CanProvisionLab || material is null || Busy || mutationBusy || day == Guid.Empty ||
            players.Count > 1000 || players.Any(p => p == Guid.Empty))
            throw new InvalidOperationException("Lab event administrator access required.");
        var actor = generation;
        Busy = true; Changed?.Invoke();
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(45) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9091/provision") {
                Content = JsonContent.Create(new { Event = day, Players = players }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", material.AccessToken);
            using var response = await http.SendAsync(request, lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Lab provisioning was not confirmed. Keep these identities and retry after checking the lab service.");
            if (actor != generation) throw new OperationCanceledException();
        }
        finally { if (actor == generation) { Busy = false; Changed?.Invoke(); } }
    }
}
