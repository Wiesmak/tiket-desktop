using System.Globalization;
using Google.Protobuf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private RaceDayConfiguration localDefinition = new();
    private LocalRaceDayPlan? localPlan;
    private string? administrationUiBinding;
    private string? commandsStamp;
    private RaceList? renderedAdminRaces;
    private bool renderingAdministration, showPlayers, inspectFrozenPlan;
    private int planRaceIndex = -1;
    private readonly Dictionary<PoolType, (TextBox Stake, TextBox Take)> poolEditors = [];
    private Guid? adminDialogReview;
    private string? administrationUiError;
    private sealed record AdminRaceOption(string Id, string Label) { public override string ToString() => Label; }

    private void InitializeAdministration() => RenderAdministration();
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
    private static int Integer(TextBox box, string label, int minimum = 1, int maximum = int.MaxValue)
    {
        if (!int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
        { box.Focus(FocusState.Programmatic); throw new InvalidOperationException(label + " is invalid."); }
        return value;
    }
    private void NewPlan()
    {
        localPlan = null; planRaceIndex = -1; localDefinition = new();
        PlanDay.Text = KnownEvents.SelectedItem?.ToString() ?? "";
        PlanDate.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        PlanNumber.Text = "1"; PlanCurrency.Text = "PLN"; PlanZone.Text = "Europe/Warsaw"; PlanRules.Text = "1";
        PlanLegs.Text = "10000"; PlanOpen.Text = "1";
        PlanValidation.Text = ""; RenderPlanRaces(-1);
    }
    private RaceConfiguration? EditingRace => planRaceIndex >= 0 && planRaceIndex < localDefinition.Races.Count ? localDefinition.Races[planRaceIndex] : null;
    private void CommitRace()
    {
        if (EditingRace is not { } race) return;
        race.Number = Integer(PlanRaceNumber, "Race number"); race.Title = PlanRaceTitle.Text; race.RacecourseName = PlanRaceCourse.Text;
        foreach (var pool in race.Pools)
            if (poolEditors.TryGetValue(pool.Type, out var boxes))
            {
                if (!ConnectionSession.TryFunding(boxes.Stake.Text, out var stake) || stake == 0)
                { boxes.Stake.Focus(FocusState.Programmatic); throw new InvalidOperationException("Enter a positive integer stake."); }
                pool.UnitStake = stake; pool.TakeBasisPoints = Integer(boxes.Take, "Take basis points", 0, 10000);
            }
    }
    private RaceDayConfiguration CaptureDefinition()
    {
        CommitRace();
        localDefinition.RaceDayId = PlanDay.Text; localDefinition.EventDate = PlanDate.Text;
        localDefinition.DisplayNumber = Integer(PlanNumber, "Day number", 1, 999); localDefinition.Currency = PlanCurrency.Text;
        localDefinition.TimeZone = PlanZone.Text; localDefinition.RulesVersion = PlanRules.Text;
        localDefinition.MaximumCouponLegs = Integer(PlanLegs, "Coupon leg limit");
        localDefinition.MaximumOpenRaces = Integer(PlanOpen, "Open-race limit");
        localDefinition.NoWinnerPolicy = NoWinnerPolicy.HouseRetainsPool;
        return localDefinition.Clone();
    }
    private void RestorePlan(LocalRaceDayPlan plan)
    {
        localPlan = plan; localDefinition = plan.Definition;
        inspectFrozenPlan = connection.Overview is not null && plan.IsFrozen;
        renderingAdministration = true;
        PlanDay.Text = localDefinition.RaceDayId; PlanDate.Text = localDefinition.EventDate;
        PlanNumber.Text = localDefinition.DisplayNumber.ToString(CultureInfo.InvariantCulture);
        PlanCurrency.Text = localDefinition.Currency; PlanZone.Text = localDefinition.TimeZone; PlanRules.Text = localDefinition.RulesVersion;
        PlanLegs.Text = localDefinition.MaximumCouponLegs.ToString(CultureInfo.InvariantCulture);
        PlanOpen.Text = localDefinition.MaximumOpenRaces.ToString(CultureInfo.InvariantCulture);
        renderingAdministration = false; RenderPlanRaces(0); RenderAdministration();
    }
    private void RenderPlanRaces(int index)
    {
        renderingAdministration = true;
        PlanRace.ItemsSource = localDefinition.Races.Select(r => new AdminRaceOption(r.RaceId, $"Race {r.Number} · {r.Title}")).ToArray();
        planRaceIndex = localDefinition.Races.Count == 0 ? -1 : Math.Clamp(index, 0, localDefinition.Races.Count - 1);
        PlanRace.SelectedIndex = planRaceIndex; renderingAdministration = false;
        RenderRaceEditor();
    }
    private void RenderRaceEditor()
    {
        renderingAdministration = true; poolEditors.Clear(); PlanPools.Children.Clear(); PlanRunners.Children.Clear();
        var race = EditingRace;
        PlanRaceId.Text = race?.RaceId ?? ""; PlanRaceNumber.Text = race?.Number.ToString(CultureInfo.InvariantCulture) ?? "";
        PlanRaceTitle.Text = race?.Title ?? ""; PlanRaceCourse.Text = race?.RacecourseName ?? "";
        RosterRaceLabel.Text = PoolsRaceLabel.Text = race is null ? "Add and select a race first." : $"Race {race.Number} · {race.Title}";
        if (race is not null)
        {
            foreach (var runner in race.Runners.ToArray())
            {
                var row = new StackPanel { Spacing = 6 };
                var id = new TextBox { Header = "Horse UUID", Text = runner.HorseId, IsReadOnly = true };
                var program = new TextBox { Header = "Program number", Text = runner.ProgramNumber, MaxLength = 128 };
                var name = new TextBox { Header = "Horse name", Text = runner.DisplayName, MaxLength = 256 };
                AutomationProperties.SetAutomationId(program, "RunnerProgram" + race.Runners.IndexOf(runner));
                AutomationProperties.SetAutomationId(name, "RunnerName" + race.Runners.IndexOf(runner));
                program.TextChanged += (_, _) => runner.ProgramNumber = program.Text;
                name.TextChanged += (_, _) => runner.DisplayName = name.Text;
                row.Children.Add(id); row.Children.Add(program); row.Children.Add(name);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var (label, delta) in new[] { ("Up", -1), ("Down", 1), ("Remove horse", 0) })
                {
                    var button = new Button { Content = label };
                    button.Click += (_, _) => {
                        var at = race.Runners.IndexOf(runner);
                        if (delta == 0) race.Runners.RemoveAt(at);
                        else if (at + delta >= 0 && at + delta < race.Runners.Count) { race.Runners.RemoveAt(at); race.Runners.Insert(at + delta, runner); }
                        RenderRaceEditor();
                    };
                    actions.Children.Add(button);
                }
                row.Children.Add(actions); PlanRunners.Children.Add(row);
            }
            foreach (var pool in race.Pools)
            {
                var stake = new TextBox { Header = pool.Type + " unit stake", Text = pool.UnitStake.ToString(CultureInfo.InvariantCulture), MaxLength = 19 };
                var take = new TextBox { Header = pool.Type + " take basis points (0–10000)", Text = pool.TakeBasisPoints.ToString(CultureInfo.InvariantCulture), MaxLength = 5 };
                AutomationProperties.SetAutomationId(stake, "PoolStake" + pool.Type);
                AutomationProperties.SetAutomationId(take, "PoolTake" + pool.Type);
                PlanPools.Children.Add(stake); PlanPools.Children.Add(take); poolEditors[pool.Type] = (stake, take);
            }
        }
        renderingAdministration = false;
        if (localPlan?.IsFrozen == true) SetPlanEditable(EditablePlanFields, false);
    }
    private void OnPlanRaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderingAdministration) return;
        try { CommitRace(); planRaceIndex = PlanRace.SelectedIndex; RenderRaceEditor(); }
        catch (Exception error) { PlanValidation.Text = error.Message; renderingAdministration = true; PlanRace.SelectedIndex = planRaceIndex; renderingAdministration = false; }
    }
    private void OnPlanRaceFields(object sender, TextChangedEventArgs e) { /* Retained in native controls until a deliberate save/step/race action. */ }
    private void OnAddPlanRace(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitRace();
            localDefinition.Races.Add(new RaceConfiguration { RaceId = Guid.NewGuid().ToString(), Number = localDefinition.Races.Select(r => r.Number).DefaultIfEmpty(0).Max() + 1,
                Pools = { new[] { PoolType.Win, PoolType.Quinella, PoolType.Exacta, PoolType.Trifecta }.Select(t => new PoolConfiguration { Type = t, UnitStake = 1 }) } });
            RenderPlanRaces(localDefinition.Races.Count - 1);
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private void OnRemovePlanRace(object sender, RoutedEventArgs e)
    { if (EditingRace is { } race) { localDefinition.Races.Remove(race); RenderPlanRaces(planRaceIndex); } }
    private void MoveRace(int delta)
    {
        try
        {
            CommitRace(); var at = planRaceIndex;
            if (at < 0 || at + delta < 0 || at + delta >= localDefinition.Races.Count) return;
            if (localPlan?.IsFrozen == true) throw new InvalidOperationException("Frozen plans cannot be reordered.");
            var reordered = new LocalRaceDayPlan(localDefinition); reordered.MoveRace(at, at + delta);
            localDefinition = reordered.Definition; RenderPlanRaces(at + delta);
            PlanValidation.Text = "Race moved · numbers updated";
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private void OnMovePlanRaceUp(object sender, RoutedEventArgs e) => MoveRace(-1);
    private void OnMovePlanRaceDown(object sender, RoutedEventArgs e) => MoveRace(1);
    private void OnAddPlanHorse(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitRace();
            if (EditingRace is not { } race) throw new InvalidOperationException("Add a race first.");
            race.Runners.Add(new Runner { HorseId = Guid.NewGuid().ToString(), ProgramNumber = (race.Runners.Count + 1).ToString("00"), DisplayName = "" });
            RenderRaceEditor();
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private LocalRaceDayPlan CurrentPlan()
    {
        if (localPlan?.IsFrozen == true) return localPlan;
        var definition = CaptureDefinition();
        if (localPlan is null) localPlan = new(definition); else localPlan.Replace(definition);
        return localPlan;
    }
    private void OnSavePlan(object sender, RoutedEventArgs e)
    {
        try { connection.SavePlan(CurrentPlan()); PlanValidation.Text = "Plan saved"; }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private void OnResumePlan(object sender, RoutedEventArgs e)
    {
        try {
            if (KnownEvents.SelectedItem is not Guid day) throw new InvalidOperationException("Select an event.");
            RestorePlan(connection.ResumePlan(day) ?? throw new InvalidOperationException("No saved plan for this operator and event."));
            PlanValidation.Text = "Plan resumed";
        }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private void OnDiscardPlan(object sender, RoutedEventArgs e)
    {
        try { if (KnownEvents.SelectedItem is Guid day) { connection.DiscardPlan(day); NewPlan(); RenderAdministration(); } }
        catch (Exception error) { PlanValidation.Text = error.Message; }
    }
    private async Task ShowAdministrationReview(Func<AdministrationReview> prepare)
    {
        try
        {
            administrationUiError = null;
            var review = prepare(); adminDialogReview = review.Id;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = review.Title,
                Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = review.Payload, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowFinancialDialogAsync(dialog) == ContentDialogResult.Primary) await connection.ConfirmAdministrationAsync(review.Id);
            else connection.CancelAdministrationReview();
        }
        catch (Exception error) { administrationUiError = error.Message; AdminOperationStatus.Text = error.Message; PlanValidation.Text = error.Message; connection.CancelAdministrationReview(); }
        finally { adminDialogReview = null; RenderAdministration(); refresh.Request(); }
    }
    private async void OnReviewCreateDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => {
        var plan = CurrentPlan();
        var errors = plan.Validate(connection.Profile!.KnownEvents, connection.ProvisionedEvents.Select(id => id.ToString()).ToArray());
        if (errors.Count > 0) { PlanValidation.Text = string.Join("\n", errors); throw new InvalidOperationException(PlanValidation.Text); }
        return connection.ReviewCreate(plan);
    });
    private async void OnPublishDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewDay(AdministrationOperation.PublishRaceDay));
    private async Task ShowStaffHistory(bool coupons) {
        try { await connection.ReadStaffHistoryAsync(coupons); StaffHistory.Text = connection.StaffHistoryText; }
        catch (Exception error) { StaffHistory.Text = error.Message; }
    }
    private async void OnPlayerHistory(object sender, RoutedEventArgs e) => await ShowStaffHistory(true);
    private async void OnRaceHistory(object sender, RoutedEventArgs e) => await ShowStaffHistory(false);
    private async void OnFinalizeDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewDay(AdministrationOperation.FinalizeRaceDay));
    private async void OnCompleteDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewDay(AdministrationOperation.CompleteRaceDay));
    private async void OnCloseDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewDay(AdministrationOperation.CloseRaceDay));
    private async void OnCashOut(object sender, RoutedEventArgs e) => await ShowAdministrationReview(connection.ReviewCashOut);
    private async void OnStartDay(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewDay(AdministrationOperation.StartRaceDay));
    private async void OnOpenRace(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewRace(AdministrationOperation.OpenBetting, ((AdminRaceOption)ServerAdminRace.SelectedItem).Id));
    private async void OnCloseRace(object sender, RoutedEventArgs e) => await ShowAdministrationReview(() => connection.ReviewRace(AdministrationOperation.CloseBetting, ((AdminRaceOption)ServerAdminRace.SelectedItem).Id));
    private void OnAdminRaceChanged(object sender, SelectionChangedEventArgs e) { if (!renderingAdministration) RenderAdministration(); }
    private async Task ReviewFunding(bool additional)
    {
        await ShowAdministrationReview(() => {
            if (!ConnectionSession.TryFunding(OpeningPaid.Text, out var paid) || !ConnectionSession.TryFunding(OpeningPromotional.Text, out var promotional))
                throw new InvalidOperationException("Enter nonnegative whole currency units.");
            return connection.ReviewEnrollment(AdminPlayer.Text, paid, promotional, additional);
        });
    }
    private async void OnEnrollPlayer(object sender, RoutedEventArgs e) => await ReviewFunding(false);
    private async void OnAddFunding(object sender, RoutedEventArgs e) => await ReviewFunding(true);
    private void OnAdminPlayerChanged(object sender, TextChangedEventArgs e)
    { if (AdminAccountStatus is not null && !renderingAdministration && State.Destination == Destination.Administration) { connection.SelectPlayer(AdminPlayer.Text); refresh.Request(); } }
    private void OnCashierPlayerChanged(object sender, TextChangedEventArgs e)
    { if (CashierAccountStatus is not null && State.Destination == Destination.Cashier) { connection.SelectPlayer(PurchasePlayer.Text); refresh.Request(); } }
    private async void OnListPlayers(object sender, RoutedEventArgs e)
    {
        try { showPlayers = true; await connection.ReadPlayersAsync(); }
        catch { AdminOperationStatus.Text = "Player list unavailable."; }
    }
    private void SetPlanEditable(DependencyObject parent, bool editable)
    {
        if (parent is not Panel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is TextBox box) box.IsReadOnly = !editable || box == PlanDay || box == PlanRaceId || Equals(box.Header, "Horse UUID");
            else if (child is Button button && button != ReviewCreateButton) button.IsEnabled = editable;
            else if (child is Panel nested) SetPlanEditable(nested, editable);
        }
    }
    private void RenderAdministration()
    {
        RenderAliases();
        NewLabEventButton.Visibility = ProvisionPlayersButton.Visibility = connection.CanProvisionLab ? Visibility.Visible : Visibility.Collapsed;
        if (LocalPlanStatus is null) return;
        var binding = connection.Subject is null ? null : string.Join("|", connection.Subject, connection.Profile?.Id, KnownEvents.SelectedItem);
        if (binding != administrationUiBinding)
        {
            activeRound = null; RoundDetails.Text = ""; PlaceRoundButton.IsEnabled = false;
            eventReport = null; reportContext = null; EventReportDetails.Text = ""; ExportEventReportButton.IsEnabled = false;
            StaffHistory.Text = ""; administrationUiBinding = binding; renderedAdminRaces = null; commandsStamp = null; showPlayers = false; administrationUiError = null;
            renderingAdministration = true; AdminPlayer.Text = ""; OpeningPaid.Text = OpeningPromotional.Text = "0";
            inspectFrozenPlan = false; NewPlan(); renderingAdministration = false;
        }
        var live = connection.Overview; var idle = !connection.MutationBusy && !connection.Busy;
        SetPlanEditable(EditablePlanFields, connection.IsEventAdmin && localPlan?.IsFrozen != true);
        EditablePlanFields.Visibility = live is null || inspectFrozenPlan ? Visibility.Visible : Visibility.Collapsed;
        SavePlanButton.Visibility = DiscardPlanButton.Visibility = live is null ? Visibility.Visible : Visibility.Collapsed;
        ResumePlanButton.Visibility = connection.IsEventAdmin ? Visibility.Visible : Visibility.Collapsed;
        ResumePlanButton.Content = live is null ? "Resume" : "Inspect saved plan";
        SavePlanButton.IsEnabled = DiscardPlanButton.IsEnabled = connection.IsEventAdmin && idle && localPlan?.IsFrozen != true;
        ResumePlanButton.IsEnabled = connection.IsEventAdmin && idle;
        LocalPlanStatus.Text = live is not null ? "Server definition — immutable" :
            localPlan?.IsFrozen == true ? "Frozen local request — create or recover it" :
            connection.UnusedEvent is not null ? "Local plan · unused provisioned slot" : "Select a provisioned unused event slot to create a day.";
        ReviewCreateButton.IsEnabled = connection.IsEventAdmin && connection.UnusedEvent is not null && idle;
        AdminOperationStatus.Text = administrationUiError ?? connection.AdministrationStatus;
        AdminDayState.Text = live is null ? "No server day loaded" : $"{live.Day.Status.Phase} · version {live.Day.Status.Version}";
        PublishDayButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Draft;
        StartDayButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Ready;
        FinalizeDayButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Running;
        CompleteDayButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Finalizing;
        CloseDayButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Completed;
        ClosureSummary.Text = live is null ? "" : $"{live.Races.Races.Count(r => r.Status.Phase is RacePhase.Settled or RacePhase.Voided)}/{live.Races.Races.Count} terminal races. Backend verifies unresolved legs and all enrolled accounts before closure.";
        if (!Equals(renderedAdminRaces, live?.Races))
        {
            var selected = (ServerAdminRace.SelectedItem as AdminRaceOption)?.Id;
            renderingAdministration = true;
            ServerAdminRace.ItemsSource = live?.Races.Races.Select(r => new AdminRaceOption(r.Definition.RaceId, $"Race {r.Definition.Number} · {r.Definition.Title}")).ToArray();
            ServerAdminRace.SelectedItem = (ServerAdminRace.ItemsSource as AdminRaceOption[])?.FirstOrDefault(r => r.Id == selected);
            if (ServerAdminRace.SelectedItem is null && live?.Races.Races.Count > 0) ServerAdminRace.SelectedIndex = 0;
            renderedAdminRaces = live?.Races.Clone(); renderingAdministration = false;
        }
        var race = live?.Races.Races.FirstOrDefault(r => r.Definition.RaceId == (ServerAdminRace.SelectedItem as AdminRaceOption)?.Id);
        RenderResults(race, idle);
        AdminRaceState.Text = race is null ? "" : $"{race.Status.Phase} · version {race.Status.Version}";
        OpenRaceButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Running &&
            race?.Status.Phase == RacePhase.Scheduled && live.Races.Races.Count(r => r.Status.Phase == RacePhase.BettingOpen) < live.Day.Definition.MaximumOpenRaces;
        CloseRaceButton.IsEnabled = connection.IsEventAdmin && idle && live?.Day.Status.Phase == DayPhase.Running && race?.Status.Phase == RacePhase.BettingOpen;
        CreatedDefinitionSummary.Visibility = inspectFrozenPlan ? Visibility.Collapsed : Visibility.Visible;
        CreatedDefinitionSummary.Text = live is null ? "" :
            $"{live.Day.Definition.EventDate} · {live.Day.Definition.Currency} · {live.Day.Definition.TimeZone}\n" +
            string.Join("\n\n", live.Races.Races.Select(r => $"Race {r.Definition.Number} · {r.Definition.Title}\n{r.Definition.RacecourseName}\n" +
                string.Join("\n", r.Definition.Runners.Select(h => h.ProgramNumber + " · " + h.DisplayName)) + "\n" +
                string.Join(" · ", r.Definition.Pools.Select(p => $"{p.Type}: {p.UnitStake}"))));
        ServerDefinition.Text = live is null ? "" : JsonFormatter.Default.Format(live.Day.Definition);
        var account = connection.CurrentAccount; var enrollment = connection.Enrollment;
        var accountText = account is null ? (string.IsNullOrEmpty(connection.AccountReadStatus) ? "Select a player" : connection.AccountReadStatus) :
            $"Balance: {account.Balance} {account.Currency} · v{account.AccountVersion}" + (connection.AccountReadStatus == "Offline" ? $" · Offline · updated {connection.AccountReadAt:t}" : "");
        AdminAccountStatus.Text = CashierAccountStatus.Text = accountText;
        StaffHistory.Text = connection.StaffHistoryText;
        AccountDetails.Text = account is null ? "" : AccountPresentation.Account(account);
        WithdrawalDetails.Text = connection.WithdrawalText;
        MovementDetails.Text = connection.AccountHistoryText;
        CashOutButton.IsEnabled = idle && connection.CanCashOut;
        var beforeStart = live?.Day.Status.Phase is DayPhase.Draft or DayPhase.Ready;
        EnrollPlayerButton.IsEnabled = connection.IsEnrollmentCashier && idle && beforeStart;
        AddFundingButton.IsEnabled = connection.IsEnrollmentCashier && idle && beforeStart && enrollment is not null && account?.AcceptedStakes == 0;
        ListPlayersButton.IsEnabled = connection.IsAuditor && live is not null && idle;
        ListPlayersButton.Visibility = connection.IsAuditor ? Visibility.Visible : Visibility.Collapsed;
        AdminPlayers.Text = showPlayers && connection.EnrolledPlayers is { } list ?
            connection.PlayerAccountSummary : "";
        if (adminDialogReview is { } reviewId && !connection.IsAdministrationReviewCurrent(reviewId)) financialDialog?.Hide();
        try
        {
            var history = KnownEvents.SelectedItem is Guid day && connection.Subject is not null ? connection.AdministrationHistory(day) : [];
            if (history.Any(h => h.CanRecover)) AdminOperationStatus.Text = "Uncertain — recover";
            var stamp = binding + string.Join("|", history.Select(h => h.Id + h.State));
            if (stamp != commandsStamp)
            {
                commandsStamp = stamp; AdminCommands.Children.Clear();
                foreach (var command in history.Take(20))
                {
                    AdminCommands.Children.Add(Text(command.Operation + " · " + command.State));
                    if (command.CanRecover)
                    {
                        var recover = new Button { Content = "Recover original command", IsEnabled = idle };
                        recover.Click += async (_, _) => {
                            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Recover original command?",
                                Content = "This can complete the original operation. It reuses the saved request and key.",
                                PrimaryButtonText = "Recover", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                            if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
                            try { await connection.RecoverAdministrationAsync(command.Id, (Guid)KnownEvents.SelectedItem); }
                            catch { AdminOperationStatus.Text = "Recovery unavailable. Restore the original operator and grants."; }
                            RenderAdministration(); refresh.Request();
                        };
                        AdminCommands.Children.Add(recover);
                    }
                }
            }
        }
        catch { AdminCommands.Children.Clear(); commandsStamp = null; }
    }
}
