using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private readonly ConnectionSession connection = new(new NativeOidcLogin(async uri =>
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            throw new InvalidOperationException("Windows could not activate the system browser.");
    }));
    private readonly RefreshCoordinator refresh = new();
    private readonly System.Diagnostics.Stopwatch refreshClock = System.Diagnostics.Stopwatch.StartNew();
    private bool foreground = true;
    private bool retryRequested;
    public void SetForeground(bool value)
    {
        foreground = value;
        if (!value) refresh.Select(null, refreshClock.Elapsed);
        else refresh.Request();
    }
    private async Task SynchronizeVisibleAsync()
    {
        var key = connection.Subject is null || KnownEvents.SelectedItem is not Guid ||
            (!retryRequested && connection.State is ConnectionState.Denied or ConnectionState.UnknownEvent) ? null :
            string.Join("|", connection.Profile?.Id, connection.Subject, KnownEvents.SelectedItem);
        refresh.Select(key, refreshClock.Elapsed);
        if (KnownEvents.SelectedItem is Guid day)
            await refresh.TickAsync(refreshClock.Elapsed, foreground && IsLoaded,
                State.Destination == Destination.Cashier, async ct => { retryRequested = false; var success = await connection.SynchronizeAsync(day, ct); if (success) {
                    connection.SelectPlayer(State.Destination == Destination.Cashier ? PurchasePlayer.Text : AdminPlayer.Text);
                    success &= await connection.ReadSelectedPlayerAsync(ct);
                    if (showPlayers && connection.IsAuditor) await connection.ReadPlayersAsync();
                    await RefreshVisiblePrintsAsync(ct);
                } return success; });
    }
    private readonly DispatcherTimer expiryTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitializeConnection()
    {
        InitializeComposer();
        connection.Changed += RenderConnection;
        connection.RefreshRequested += () => { retryRequested = true; refresh.Request(); };
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
        expiryTimer.Tick += async (_, _) => { connection.CheckExpiry(); await SynchronizeVisibleAsync(); };
        expiryTimer.Start();
        Unloaded += (_, _) => { expiryTimer.Stop(); refresh.Dispose(); connection.Dispose(); };
    }
    public void CloseConnection() { expiryTimer.Stop(); refresh.Dispose(); connection.Dispose(); }

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
    private async void OnSignIn(object sender, RoutedEventArgs e) { await connection.SignInAsync(); await SynchronizeVisibleAsync(); }
    private async void OnPing(object sender, RoutedEventArgs e) => await connection.PingAsync();
    private async void OnReadEvent(object sender, RoutedEventArgs e)
    {
        retryRequested = true; refresh.Request(); await SynchronizeVisibleAsync();
    }
    private void OnSignOut(object sender, RoutedEventArgs e) => connection.SignOut();
    private void OnCancelConnection(object sender, RoutedEventArgs e) => connection.Cancel();

    private void RenderConnectionControls()
    {
        SignInButton.IsEnabled = PingButton.IsEnabled = connection.Profile is not null && !connection.Busy;
        ReadButton.IsEnabled = connection.Subject is not null && !connection.Busy;
        ReadButton.Visibility = connection.State is ConnectionState.Denied or ConnectionState.UnknownEvent or ConnectionState.Offline or ConnectionState.Error ? Visibility.Visible : Visibility.Collapsed;
        KnownEvents.IsEnabled = connection.Profile is not null && !connection.Busy;
        CancelButton.IsEnabled = connection.Busy;
        SignOutButton.IsEnabled = connection.Subject is not null || connection.Busy;
        foreach (var field in new[] { ProfileDetails, OperatorStatus, PingDetails, EventDetails })
            field.Visibility = string.IsNullOrEmpty(field.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RenderConnection()
    {
        SessionStatus.Text = connection.State + " · " + ConnectionErrors.Message(connection.State) +
            (connection.SafeDiagnostic is null ? "" : "\n" + connection.SafeDiagnostic);
        OperatorStatus.Text = connection.Subject is null ? "" : $"Operator: {connection.Subject} · expires {connection.ExpiresAt:g}";
        PingDetails.Text = connection.PingStatus ?? "";
        var live = connection.Overview;
        EventDetails.Text = "";
        CashierLiveContext.Text = AdminLiveContext.Text = "";
        CashierLiveContext.Visibility = AdminLiveContext.Visibility = live is null ? Visibility.Collapsed : Visibility.Visible;
        if (live is not null)
        {
            var day = live.Day;
            var context = (connection.State == ConnectionState.Offline ? "Offline · " : "") + $"Day {day.Definition.DisplayNumber} · {day.Status.Phase} · updated {(connection.LastSynchronizedAt ?? live.ReadAt).ToLocalTime():t}";
            CashierLiveContext.Text = AdminLiveContext.Text = context;
            EventDetails.Text = context + $"\n{day.Definition.EventDate} · {day.Definition.Currency} · {day.Definition.TimeZone}\n" +
                string.Join("\n", live.Races.Races.Select(r =>
                    $"Race {r.Definition.Number}: {r.Definition.RaceId} · {r.Status.Phase} · version {r.Status.Version}\n" +
                    string.Join("\n", r.Definition.Runners.Select(h => $"  {h.ProgramNumber} · {h.HorseId} · {h.DisplayName}"))));
        }
        if (live is null)
        {
            var message = connection.State switch {
                ConnectionState.Denied => "Access denied", ConnectionState.Expired => "Session expired — sign in",
                ConnectionState.Offline => "Offline", ConnectionState.Connecting => "Loading…", _ => "" };
            CashierLiveContext.Text = AdminLiveContext.Text = message;
            CashierLiveContext.Visibility = AdminLiveContext.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
        RenderConnectionControls();
        RefreshComposerContext();
        RenderPurchases();
        RenderAdministration();
    }
}
