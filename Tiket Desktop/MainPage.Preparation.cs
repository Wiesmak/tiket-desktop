using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private PlayerAliasStore? aliasStore;
    private string? aliasBinding;
    private bool renderingAliases;
    private PlayerAliasStore Aliases => aliasStore ??= new();
    private CancellationTokenSource? rosterCancellation;
    private async void OnEnrollRoster(object sender, RoutedEventArgs e)
    {
        if (rosterCancellation is not null) return;
        try
        {
            var (profile, day) = AliasContext(); var rows = Aliases.Load(profile, day);
            if (rows.Count == 0) throw new InvalidOperationException("Prepare the player roster first.");
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Enroll prepared players?",
                Content = $"Event {day:D}\n{rows.Count} players · each new account: 0 paid + 200 promotional\nMaximum new opening total: {checked(rows.Count * 200L)} PLN\nExisting enrollments are read and skipped. Each new enrollment is independently journaled.",
                PrimaryButtonText = "Enroll roster", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
            rosterCancellation = new(); EnrollRosterButton.IsEnabled = false; CancelRosterButton.IsEnabled = true;
            await connection.EnrollRosterAsync(rows, message => AliasStatus.Text = message, rosterCancellation.Token);
            AliasStatus.Text = "Roster enrollment confirmed. " + connection.PlayerAccountSummary;
            showPlayers = true;
        }
        catch (OperationCanceledException) { AliasStatus.Text = "Stopped. Resume reads the same roster and server enrollments."; }
        catch (Exception error) { AliasStatus.Text = error.Message; }
        finally { rosterCancellation?.Dispose(); rosterCancellation = null; EnrollRosterButton.IsEnabled = true; CancelRosterButton.IsEnabled = false; refresh.Request(); }
    }
    private void OnCancelRoster(object sender, RoutedEventArgs e) => rosterCancellation?.Cancel();
    private async void OnNewLabEvent(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!connection.CanProvisionLab || connection.Profile is not { } profile) throw new InvalidOperationException("Sign in as a lab event administrator first.");
            Guid? completed = null;
            if (connection.Overview?.Day.Status.Phase is Pl.Umamusume.Tiket.Api.V1.DayPhase.Completed or Pl.Umamusume.Tiket.Api.V1.DayPhase.Closed)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Prepare another event?", Content = "The selected event is complete. Its accounts, journals and report will remain available. Prepare a new event with new player identities?",
                    PrimaryButtonText = "Prepare new event", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
                completed = Guid.Parse(connection.Overview.Day.Definition.RaceDayId);
            }
            var day = Aliases.PendingEvent(profile.Id, completed);
            var rows = Aliases.PrepareGroup(profile.Id, day);
            await ProvisionAndReissue(day, rows);
            await connection.SynchronizeAsync(day, default);
            if (connection.UnusedEvent == day)
            {
                var saved = connection.ResumePlan(day);
                if (saved is null) { saved = new(StaffPreparation.SixRaceTemplate(day, DateOnly.FromDateTime(DateTime.Today))); connection.SavePlan(saved); }
                RestorePlan(saved);
                PlanValidation.Text = "Event and 24 player grants provisioned and reissued. Review/create this saved plan, enroll the roster, then publish/start.";
            }
            else PlanValidation.Text = "Existing event resumed. Continue its original operations.";
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private async Task ProvisionAndReissue(Guid day, IReadOnlyList<PlayerAlias> rows)
    {
        await connection.ProvisionLabAsync(day, rows.Select(p => p.Player).ToArray());
        await connection.SignInAsync();
        if (connection.Subject is null || !connection.ProvisionedEvents.Contains(day)) throw new InvalidOperationException("Updated signed event access was not issued. Resume the same preparation after sign-in.");
        if (!Equals(KnownEvents.SelectedItem, day)) KnownEvents.SelectedItem = day;
        connection.SelectEvent(day); RenderAliases(true);
    }
    private async void OnProvisionPlayers(object sender, RoutedEventArgs e)
    {
        try { var (profile, day) = AliasContext(); await ProvisionAndReissue(day, Aliases.Load(profile, day)); refresh.Request(); }
        catch (Exception error) { AliasStatus.Text = error.Message; }
    }

    private void OnSixRaceTemplate(object sender, RoutedEventArgs e)
    {
        try
        {
            if (localPlan?.IsFrozen == true) throw new InvalidOperationException("This plan is frozen. Resume its original command.");
            if (!Guid.TryParseExact(PlanDay.Text, "D", out var day) ||
                !DateOnly.TryParseExact(PlanDate.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Select an event and enter its date first.");
            if (localDefinition.Races.Count != 0) throw new InvalidOperationException("The template requires an empty plan; discard the editable plan first.");
            localDefinition = StaffPreparation.SixRaceTemplate(day, date, PlanCurrency.Text);
            localPlan = new(localDefinition); connection.SavePlan(localPlan);
            RestorePlan(localPlan);
            PlanValidation.Text = "Saved six races: 6/6/6/12/12/12 horses · four pools · unit stake 1 · take 10%. Review before creation.";
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }

    private (string Profile, Guid Day) AliasContext()
    {
        if (connection.Profile is not { } profile || KnownEvents.SelectedItem is not Guid day)
            throw new InvalidOperationException("Select a profile and event first.");
        return (profile.Id, day);
    }
    private void RenderAliases(bool force = false)
    {
        var binding = connection.Profile?.Id + "|" + KnownEvents.SelectedItem;
        if (!force && binding == aliasBinding) return;
        aliasBinding = binding;
        try
        {
            var (profile, day) = AliasContext();
            var rows = Aliases.Load(profile, day);
            var selected = (AliasPlayers.SelectedItem as PlayerAlias)?.Player;
            renderingAliases = true;
            AliasPlayers.ItemsSource = rows;
            var cashierSelected = (CashierAliasPlayers.SelectedItem as PlayerAlias)?.Player;
            CashierAliasPlayers.ItemsSource = rows;
            CashierAliasPlayers.SelectedItem = rows.FirstOrDefault(p => p.Player == cashierSelected);
            AliasPlayers.SelectedItem = rows.FirstOrDefault(p => p.Player == selected);
            PlayerDisplayName.Text = (AliasPlayers.SelectedItem as PlayerAlias)?.DisplayName ?? "";
            AliasStatus.Text = $"{rows.Count} local aliases · enrollment and balances must be read from the server.";
        }
        catch (Exception error) { AliasPlayers.ItemsSource = CashierAliasPlayers.ItemsSource = null; PlayerDisplayName.Text = ""; AliasStatus.Text = error.Message; }
        finally { renderingAliases = false; }
    }
    private void OnPreparePlayerAliases(object sender, RoutedEventArgs e)
    {
        try { var (profile, day) = AliasContext(); Aliases.PrepareGroup(profile, day); RenderAliases(true); }
        catch (Exception error) { AliasStatus.Text = error.Message; }
    }
    private void OnAliasPlayerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderingAliases || AliasPlayers.SelectedItem is not PlayerAlias player) return;
        PlayerDisplayName.Text = player.DisplayName;
        AdminPlayer.Text = PurchasePlayer.Text = player.Player.ToString("D");
    }
    private void OnSavePlayerAlias(object sender, RoutedEventArgs e)
    {
        try
        {
            var (profile, day) = AliasContext();
            if (AliasPlayers.SelectedItem is not PlayerAlias selected) throw new InvalidOperationException("Select a player first.");
            Aliases.Save(profile, day, Aliases.Load(profile, day).Select(p => p.Player == selected.Player ? p with { DisplayName = PlayerDisplayName.Text.Trim() } : p).ToArray());
            RenderAliases(true);
        }
        catch (Exception error) { AliasStatus.Text = error.Message; }
    }
    private void OnCashierAliasPlayerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderingAliases || CashierAliasPlayers.SelectedItem is not PlayerAlias player) return;
        PurchasePlayer.Text = player.Player.ToString("D");
    }
    private void OnPromotionalOpening(object sender, RoutedEventArgs e)
    { OpeningPaid.Text = "0"; OpeningPromotional.Text = "200"; }
}
