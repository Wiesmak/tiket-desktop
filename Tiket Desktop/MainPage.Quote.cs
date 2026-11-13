using System.Collections.Immutable;
using SelectionMode = Tiket.Desktop.Application.SelectionMode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private bool renderingComposer;
    private EventOverview? composerOverview;
    private RaceInformation? selectedQuoteRace;
    private string? lastQuoteRaceId;
    private sealed record RaceOption(RaceInformation Race)
    {
        public override string ToString() => $"Race {Race.Definition.Number} · {Race.Definition.Title} · {Race.Definition.RacecourseName} · {Race.Status.Phase}";
    }
    private sealed record HorseOption(string? Id, string Label, bool Wall = false)
    {
        public override string ToString() => Label;
    }
    private void InitializeComposer()
    {
        connection.Composer.Changed += RenderQuote;
        RenderQuote();
    }
    private async void OnKnownEventChanged(object sender, SelectionChangedEventArgs e)
    {
        refresh.Select(null, refreshClock.Elapsed);
        retryRequested = true;
        if (KnownEvents.SelectedItem is Guid day) connection.SelectEvent(day);
        else connection.ClearEventContext();
        composerOverview = null; selectedQuoteRace = null;
        connection.Composer.SetContext(null, null);
        renderingComposer = true; QuoteRace.ItemsSource = null; QuotePool.ItemsSource = null;
        renderingComposer = false;
        BuildChoices();
        ComposerAvailability.Text = "Loading event…";
        await SynchronizeVisibleAsync();
    }
    private void RefreshComposerContext()
    {
        if (connection.Overview is { } overview && KnownEvents.SelectedItem is Guid selectedDay && overview.Day.Definition.RaceDayId != selectedDay.ToString()) return;
        if (ReferenceEquals(composerOverview, connection.Overview)) { RenderQuote(); return; }
        var previousId = lastQuoteRaceId;
        composerOverview = connection.Overview;
        renderingComposer = true;
        QuoteRace.ItemsSource = composerOverview?.Races.Races.Select(r => new RaceOption(r)).ToArray();
        QuoteRace.SelectedItem = (QuoteRace.ItemsSource as RaceOption[])?.FirstOrDefault(r => r.Race.Definition.RaceId == previousId);
        if (QuoteRace.SelectedItem is null && composerOverview is not null) QuoteRace.SelectedIndex = 0;
        renderingComposer = false;
        SetRace();
        ComposerAvailability.Visibility = composerOverview is null ? Visibility.Visible : Visibility.Collapsed;
        ComposerAvailability.Text = composerOverview is null ? "Sign in and read an authorized known event in Settings." :
            "Ready";
    }
    private void OnQuoteRace(object sender, SelectionChangedEventArgs e) { if (!renderingComposer) SetRace(); }
    private void SetRace()
    {
        var next = (QuoteRace.SelectedItem as RaceOption)?.Race;
        bool sameRace = next?.Definition.RaceId == lastQuoteRaceId;
        if (next is not null) lastQuoteRaceId = next.Definition.RaceId;
        selectedQuoteRace = next;
        renderingComposer = true;
        QuotePool.ItemsSource = next?.Definition.Pools.Select(p => p.Type).Where(p => CashierComposer.Arity(p) > 0).Distinct().ToArray();
        QuotePool.SelectedItem = connection.Composer.Draft.Pool;
        if (QuotePool.SelectedIndex < 0 && next is not null) QuotePool.SelectedIndex = 0;
        renderingComposer = false;
        connection.Composer.SynchronizeContext(composerOverview?.Day, next);
        if (next is not null && !sameRace) connection.Composer.Edit(QuoteDraft.Empty with { Units = QuoteUnits.Text });
        ChangePool(sameRace);
    }
    private void OnQuotePool(object sender, SelectionChangedEventArgs e) { if (!renderingComposer) ChangePool(false); }
    private void ChangePool(bool preserve)
    {
        if (QuotePool.SelectedItem is not PoolType pool) { BuildChoices(); return; }
        var draft = connection.Composer.Draft;
        renderingComposer = true;
        QuoteMode.ItemsSource = pool == PoolType.Win ? new[] { SelectionMode.Normal, SelectionMode.Box } :
            new[] { SelectionMode.Normal, SelectionMode.Wall, SelectionMode.Box };
        QuoteMode.SelectedItem = pool == PoolType.Win && draft.Mode == SelectionMode.Wall ? SelectionMode.Normal : draft.Mode;
        renderingComposer = false;
        if (!preserve || draft.Pool != pool) connection.Composer.Edit(draft with { Pool = pool,
            Mode = (SelectionMode)QuoteMode.SelectedItem, Slots = Enumerable.Range(0, CashierComposer.Arity(pool)).Select(_ => new SlotChoice()).ToImmutableArray(), Box = [] });
        OrderGuidance.Text = pool switch {
            PoolType.Win => "WIN: first finisher. Box requires at least two horses.",
            PoolType.Quinella => "QUINELLA: first two finishers in either order.",
            PoolType.Exacta => "EXACTA: first and second in this exact order.",
            _ => "TRIFECTA: first, second and third in this exact order." };
        BuildChoices();
    }
    private void OnQuoteMode(object sender, SelectionChangedEventArgs e)
    {
        if (renderingComposer || QuoteMode.SelectedItem is not SelectionMode mode) return;
        var draft = connection.Composer.Draft;
        connection.Composer.Edit(draft with { Mode = mode, Slots = Enumerable.Range(0, CashierComposer.Arity(draft.Pool)).Select(_ => new SlotChoice()).ToImmutableArray(), Box = [] });
        BuildChoices();
    }
    private void BuildChoices()
    {
        renderingComposer = true;
        QuoteSlots.Children.Clear(); QuoteBox.Items.Clear();
        var draft = connection.Composer.Draft;
        bool box = draft.Mode == SelectionMode.Box;
        QuoteBox.Visibility = BoxCount.Visibility = box ? Visibility.Visible : Visibility.Collapsed;
        if (selectedQuoteRace is { } race)
        {
            var horses = race.Definition.Runners.Select(r => new HorseOption(r.HorseId,
                CashierComposer.RunnerLabel(r, race.Status.ScratchedHorseIds.Contains(r.HorseId)))).ToArray();
            if (box)
            {
                foreach (var horse in horses)
                {
                    var item = new ListViewItem { Content = horse, IsEnabled = !race.Status.ScratchedHorseIds.Contains(horse.Id!) };
                    QuoteBox.Items.Add(item);
                    if (draft.Box.Contains(horse.Id!)) QuoteBox.SelectedItems.Add(item);
                }
            }
            else for (int i = 0; i < draft.Slots.Length; i++)
            {
                int slot = i;
                var control = new ComboBox { Header = draft.Pool == PoolType.Quinella ? $"Horse {i + 1} (either order)" : new[] { "First", "Second", "Third" }[i],
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                if (draft.Mode == SelectionMode.Wall) control.Items.Add(new ComboBoxItem { Content = new HorseOption(null, "WALL · eligible remaining horses", true) });
                foreach (var horse in horses) control.Items.Add(new ComboBoxItem { Content = horse, IsEnabled = !race.Status.ScratchedHorseIds.Contains(horse.Id!) });
                control.SelectedItem = control.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Content is HorseOption h &&
                    (draft.Slots[slot].IsWall ? h.Wall : !h.Wall && h.Id == draft.Slots[slot].HorseId));
                control.SelectionChanged += (_, _) => {
                    if (renderingComposer || (control.SelectedItem as ComboBoxItem)?.Content is not HorseOption h) return;
                    connection.Composer.Edit(connection.Composer.Draft with { Slots = connection.Composer.Draft.Slots.SetItem(slot, new SlotChoice(h.Id, h.Wall)) });
                };
                QuoteSlots.Children.Add(control);
            }
        }
        renderingComposer = false; RenderQuote();
    }
    private void OnQuoteBox(object sender, SelectionChangedEventArgs e)
    {
        if (renderingComposer) return;
        // Roster order is stable; selection-click order does not change the box.
        var ids = QuoteBox.Items.Cast<ListViewItem>().Where(i => QuoteBox.SelectedItems.Contains(i))
            .Select(i => ((HorseOption)i.Content).Id!).ToImmutableArray();
        connection.Composer.Edit(connection.Composer.Draft with { Box = ids });
    }
    private void OnQuoteUnits(object sender, TextChangedEventArgs e)
    {
        if (QuoteFeedback is null || renderingComposer) return;
        connection.Composer.Edit(connection.Composer.Draft with { Units = QuoteUnits.Text });
    }
    private async void OnGetQuote(object sender, RoutedEventArgs e) { await connection.QuoteAsync(); refresh.Request(); }
    private void OnCancelQuote(object sender, RoutedEventArgs e) => connection.Composer.Cancel();
    private void RenderQuote()
    {
        if (QuoteFeedback is null) return;
        var editor = connection.Composer;
        bool available = composerOverview is not null;
        CashierQuotationHeading.Text = available ?
            "New wager" : "Cashier quotation · disconnected";
        QuoteRace.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        foreach (var field in new FrameworkElement[] { QuotePool, QuoteMode, QuoteUnits, OrderGuidance })
            field.Visibility = selectedQuoteRace is not null ? Visibility.Visible : Visibility.Collapsed;
        QuoteFeedback.Text = editor.State + " · " + editor.Message;
        bool busy = editor.State == QuoteState.Quoting;
        GetQuoteButton.IsEnabled = !busy && connection.Subject is not null && !connection.Busy && editor.Validation() is null;
        CancelQuoteButton.IsEnabled = busy;
        QuoteProgress.IsActive = busy; QuoteProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BoxCount.Text = $"{editor.Draft.Box.Length} horses selected · minimum {Math.Max(2, CashierComposer.Arity(editor.Draft.Pool))}";
        var response = editor.Response;
        QuoteSummary.Visibility = QuoteDetailsExpander.Visibility = response is null ? Visibility.Collapsed : Visibility.Visible;
        if (response is not null)
        {
            var p = response.Pricing;
            QuoteSummary.Text = p is null ? "Pricing unavailable." :
                $"{p.BetType} · {CashierComposer.Canonical(p.Selection, selectedQuoteRace?.Definition.Runners ?? new Google.Protobuf.Collections.RepeatedField<Runner>())}\n" +
                $"{p.TotalStake} {p.Currency} · {p.CombinationCount} combinations · {p.UnitsPerCombination} units each\nQuoted {editor.QuotedAt:t}";
            QuoteDetails.Text = p is null ? "" : $"Unit stake: {p.UnitStake} {p.Currency}\nTotal units: {p.TotalUnits}\nDay v{response.RaceDayVersion} · race v{response.RaceVersion}";
        }
        RenderPurchases();
    }
}
