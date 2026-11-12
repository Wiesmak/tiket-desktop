using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private readonly ConnectionSession connection = new();
    private readonly DispatcherTimer expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitializeConnection()
    {
        connection.Changed += RenderConnection;
        try
        {
            Profiles.ItemsSource = ManagedProfiles.Load(ManagedProfiles.PathOnMachine);
            SessionStatus.Text = ConnectionErrors.Message(ConnectionState.Unconfigured);
        }
        catch (Exception)
        {
            SessionStatus.Text = "Missing or invalid managed profiles. Administrator: check " + ManagedProfiles.PathOnMachine;
        }
        // Selection is explicit, including after restart. Tokens and profile preferences are not persisted.
        RenderConnectionControls();
        expiryTimer.Tick += (_, _) => connection.CheckExpiry();
        expiryTimer.Start();
        Unloaded += (_, _) => { expiryTimer.Stop(); connection.Dispose(); };
    }
    public void CloseConnection() { expiryTimer.Stop(); connection.Dispose(); }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Profiles.SelectedItem is not ConnectionProfile profile) return;
        connection.Select(profile);
        ProfileDetails.Text = $"Backend: {profile.Backend}\nIssuer: {profile.Issuer}\nPublic client: {profile.ClientId}\nAPI audience: {profile.ApiAudience}" +
            (profile.DevelopmentLoopback ? "\nDevelopment loopback h2c enabled." : "");
        RenderConnectionControls();
        KnownEvents.ItemsSource = profile.KnownEvents;
        KnownEvents.SelectedIndex = profile.KnownEvents.Length > 0 ? 0 : -1;
    }
    private async void OnSignIn(object sender, RoutedEventArgs e) => await connection.SignInAsync();
    private async void OnPing(object sender, RoutedEventArgs e) => await connection.PingAsync();
    private async void OnReadEvent(object sender, RoutedEventArgs e)
    {
        if (KnownEvents.SelectedItem is Guid id) await connection.ReadAsync(id);
    }
    private void OnSignOut(object sender, RoutedEventArgs e) => connection.SignOut();
    private void OnCancelConnection(object sender, RoutedEventArgs e) => connection.Cancel();

    private void RenderConnectionControls()
    {
        SignInButton.IsEnabled = PingButton.IsEnabled = connection.Profile is not null && !connection.Busy;
        ReadButton.IsEnabled = connection.Subject is not null && !connection.Busy;
        KnownEvents.IsEnabled = connection.Profile is not null && !connection.Busy;
        CancelButton.IsEnabled = connection.Busy;
        SignOutButton.IsEnabled = connection.Subject is not null || connection.Busy;
        foreach (var field in new[] { ProfileDetails, OperatorStatus, PingDetails, EventDetails })
            field.Visibility = string.IsNullOrEmpty(field.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RenderConnection()
    {
        SessionStatus.Text = connection.State + " · " + ConnectionErrors.Message(connection.State);
        OperatorStatus.Text = connection.Subject is null ? "" : $"Operator: {connection.Subject} · expires {connection.ExpiresAt:g}";
        PingDetails.Text = connection.PingStatus ?? "";
        var live = connection.Overview;
        EventDetails.Text = "";
        CashierLiveContext.Text = AdminLiveContext.Text = "";
        CashierLiveContext.Visibility = AdminLiveContext.Visibility = live is null ? Visibility.Collapsed : Visibility.Visible;
        if (live is not null)
        {
            var day = live.Day;
            var context = $"SERVER · {day.Definition.RaceDayId} · {day.Status.Phase} · version {day.Status.Version} · read {live.ReadAt:g}";
            CashierLiveContext.Text = AdminLiveContext.Text = context;
            EventDetails.Text = context + $"\n{day.Definition.EventDate} · {day.Definition.Currency} · {day.Definition.TimeZone}\n" +
                string.Join("\n", live.Races.Races.Select(r =>
                    $"Race {r.Definition.Number}: {r.Definition.RaceId} · {r.Status.Phase} · version {r.Status.Version}\n" +
                    string.Join("\n", r.Definition.Runners.Select(h => $"  {h.ProgramNumber} · {h.HorseId} · {h.DisplayName}"))));
        }
        RenderConnectionControls();
    }
}
