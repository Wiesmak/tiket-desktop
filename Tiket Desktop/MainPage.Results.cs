using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private string? resultsEditorStamp;
    private readonly List<ComboBox> finisherBoxes = [];
    private sealed record ResultHorse(string Id, string Label) { public override string ToString() => Label; }
    private ResultHorse[] eligibleResultHorses = [];
    private string SelectedResultRace => ((AdminRaceOption)ServerAdminRace.SelectedItem).Id;
    private void AddFinisher(string? selected = null)
    {
        var row = new StackPanel { Spacing = 4 };
        var box = new ComboBox { Header = "Place " + (finisherBoxes.Count + 1), ItemsSource = eligibleResultHorses, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(box, "ResultPlace" + (finisherBoxes.Count + 1));
        box.SelectedItem = eligibleResultHorses.FirstOrDefault(h => h.Id == selected);
        finisherBoxes.Add(box); row.Children.Add(box);
        var remove = new Button { Content = "Remove place" };
        remove.Click += (_, _) => { finisherBoxes.Remove(box); FinisherEditors.Children.Remove(row);
            for (int i = 0; i < finisherBoxes.Count; i++) { finisherBoxes[i].Header = "Place " + (i + 1); AutomationProperties.SetAutomationId(finisherBoxes[i], "ResultPlace" + (i + 1)); } };
        row.Children.Add(remove); FinisherEditors.Children.Add(row);
    }
    private void OnAddFinisher(object sender, RoutedEventArgs e) => AddFinisher();
    private async void OnScratchHorse(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() =>
        connection.ReviewResultOperation(AdministrationOperation.ScratchHorse, SelectedResultRace, horse: (ScratchRunner.SelectedItem as ResultHorse)?.Id));
    private async void OnVoidRace(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() =>
        connection.ReviewResultOperation(AdministrationOperation.VoidRace, SelectedResultRace));
    private async void OnSubmitResult(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() =>
        connection.ReviewResultOperation(AdministrationOperation.SubmitResult, SelectedResultRace, ResultRevision.Text,
            ResultKind.SelectedIndex == 0 ? OfficialResultKind.Normal : OfficialResultKind.Exceptional,
            finisherBoxes.Select(b => (b.SelectedItem as ResultHorse)?.Id ?? "")));
    private async void OnVerifyResult(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() =>
        connection.ReviewResultOperation(AdministrationOperation.VerifyResult, SelectedResultRace));
    private async void OnSettleRace(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() =>
        connection.ReviewResultOperation(AdministrationOperation.SettleRace, SelectedResultRace));
    private void RenderResults(RaceInformation? race, bool idle)
    {
        connection.SelectResultRace(race?.Definition.RaceId);
        var stamp = administrationUiBinding + "|" + race?.Definition.RaceId + "|" + string.Join(",", race?.Status.ScratchedHorseIds ?? []);
        if (stamp != resultsEditorStamp) {
            resultsEditorStamp = stamp;
            eligibleResultHorses = race?.Definition.Runners.Where(h => !race.Status.ScratchedHorseIds.Contains(h.HorseId))
                .Select(h => new ResultHorse(h.HorseId, h.ProgramNumber + " · " + h.DisplayName + " · " + h.HorseId)).ToArray() ?? [];
            ScratchRunner.ItemsSource = eligibleResultHorses;
            ResultRevision.Text = ""; finisherBoxes.Clear(); FinisherEditors.Children.Clear();
            for (int i = 0; i < Math.Min(3, eligibleResultHorses.Length); i++) AddFinisher();
        }
        var phase = race?.Status.Phase;
        var beforeVerification = phase is RacePhase.Scheduled or RacePhase.BettingOpen or RacePhase.BettingClosed or RacePhase.ResultProposed;
        ScratchButton.IsEnabled = VoidButton.IsEnabled = idle && connection.IsEventAdmin && beforeVerification;
        SubmitResultButton.IsEnabled = idle && connection.HasResultRole("RESULT_SUBMITTER") && phase is RacePhase.BettingClosed or RacePhase.ResultProposed;
        VerifyResultButton.IsEnabled = idle && connection.HasResultRole("RESULT_VERIFIER") && phase == RacePhase.ResultProposed && connection.LatestResult is not null;
        SettleRaceButton.IsEnabled = idle && connection.HasResultRole("SETTLEMENT_OPERATOR") && phase == RacePhase.ResultVerified && connection.LatestResult is not null;
        var result = connection.LatestResult?.Result;
        LatestResultText.Text = result is null ? "" : $"Revision {result.Revision} · {result.Kind} · {result.Status}\n" +
            string.Join("\n", result.FinisherIds.Select((id, i) => $"{i + 1}. {id}"));
        WinnersStatus.Text = connection.ResultsStatus;
        WinnersText.Text = "";
        if (connection.IsAuditor && connection.Winners is { } report) {
            var currency = connection.Overview?.Day.Definition.Currency;
            var lines = new List<string>();
            if (report.Settlement is { } settlement) {
                lines.Add("Settlement " + settlement.SettlementId + " · result " + settlement.ResultRevision);
                lines.AddRange(settlement.Pools.Select(p => $"{p.Type}: valid stake {p.ValidStake} {currency} · winning units {p.WinningUnits} · dividend/unit {p.DividendPerUnit} {currency} · distributed {p.Distributed} {currency} · house {p.HouseAllocation} {currency}"));
                lines.AddRange(settlement.Players.Select(p => $"Player {p.PlayerId} · total awarded {p.Amount} {currency}"));
            }
            foreach (var player in report.Rows.GroupBy(r => r.Player)) {
                lines.Add("Player " + player.Key);
                foreach (var pool in player.GroupBy(r => r.Type)) {
                    lines.Add(pool.Key.ToString());
                    lines.AddRange(pool.Select(r => $"Coupon {r.Coupon} · leg {r.Ordinal}\n{r.Combination}\nWinning leg stake {r.LegStake} {r.Currency} · whole coupon stake {r.CouponStake} {r.Currency} · awarded {r.Award} {r.Currency}"));
                }
            }
            if (report.Rows.Count == 0) lines.Add(report.Complete ? "No winning legs" : "No accessible winning legs");
            WinnersText.Text = string.Join("\n\n", lines);
        }
    }
}
