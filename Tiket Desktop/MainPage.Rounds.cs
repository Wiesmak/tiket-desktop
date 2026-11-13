using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private RaceRound? activeRound;
    private CancellationTokenSource? roundCancellation;
    private async void OnPlanRound(object sender, RoutedEventArgs e)
    {
        if (roundCancellation is not null) return;
        try
        {
            var (profile, day) = AliasContext();
            if (ServerAdminRace.SelectedItem is not AdminRaceOption race || connection.PointOfSales.Count == 0)
                throw new InvalidOperationException("Select a race and sign in with cashier/POS access.");
            roundCancellation = new(); PlanRoundButton.IsEnabled = PlaceRoundButton.IsEnabled = false;
            activeRound = null;
            activeRound = await connection.PlanRoundAsync(race.Id, connection.PointOfSales[0], Aliases.Load(profile, day),
                message => RoundDetails.Text = message, roundCancellation.Token);
            RoundDetails.Text = connection.RoundSummary(activeRound);
            PlaceRoundButton.IsEnabled = true;
        }
        catch (Exception error) { RoundDetails.Text = error.Message; }
        finally { roundCancellation?.Dispose(); roundCancellation = null; PlanRoundButton.IsEnabled = true; }
    }
    private async void OnPlaceRound(object sender, RoutedEventArgs e)
    {
        if (roundCancellation is not null || activeRound is null) return;
        try
        {
            if ((ServerAdminRace.SelectedItem as AdminRaceOption)?.Id != activeRound.Race)
                throw new InvalidOperationException("Resume or plan the currently selected race before placing bets.");
            RoundDetails.Text = connection.RoundSummary(activeRound);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Place reviewed round bets?",
                Content = "Confirm the round shown behind this dialog. Each eligible player has one independently journaled purchase. Confirmed purchases are skipped on resume. UNKNOWN commands require explicit original-request recovery.",
                PrimaryButtonText = "Place round bets", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
            roundCancellation = new(); PlanRoundButton.IsEnabled = PlaceRoundButton.IsEnabled = false; StopRoundButton.IsEnabled = true;
            await connection.PlaceRoundAsync(activeRound, message => RoundDetails.Text = message, roundCancellation.Token);
            RoundDetails.Text = connection.RoundSummary(activeRound);
        }
        catch (OperationCanceledException) { RoundDetails.Text += "\nStopped between purchases. Resume this saved round."; }
        catch (Exception error) { RoundDetails.Text += "\n" + error.Message; }
        finally { roundCancellation?.Dispose(); roundCancellation = null; PlanRoundButton.IsEnabled = PlaceRoundButton.IsEnabled = true; StopRoundButton.IsEnabled = false; refresh.Request(); }
    }
    private void OnStopRound(object sender, RoutedEventArgs e) => roundCancellation?.Cancel();
}
