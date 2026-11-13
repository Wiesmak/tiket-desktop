using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private sealed class PrintWatch(Guid id, string day, int pos, TextBlock status)
    {
        public Guid Id = id;
        public string Day = day;
        public int Pos = pos;
        public TextBlock Status = status;
        public Button? Reprint;
        public bool Terminal;
    }
    private readonly List<PrintWatch> printWatches = [];
    private async Task RefreshVisiblePrintsAsync(CancellationToken cancellation)
    {
        if (State.Destination != Tiket.Desktop.Application.Destination.Cashier) return;
        foreach (var watch in printWatches.Where(w => !w.Terminal).Take(10).ToArray())
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var value = await connection.RefreshPrintAsync(watch.Id, watch.Day, watch.Pos, cancellation);
                if (cancellation.IsCancellationRequested || !printWatches.Contains(watch)) return;
                watch.Status.Text = value;
                watch.Terminal = value.Contains("· Dispatched ·", StringComparison.Ordinal) ||
                    value.Contains("· PermanentlyFailed ·", StringComparison.Ordinal);
                if (watch.Reprint is not null) watch.Reprint.IsEnabled = connection.CanRequestReprint(watch.Id);
            }
            catch (OperationCanceledException) { throw; }
            catch { if (printWatches.Contains(watch)) watch.Status.Text = "Print status unavailable — retrying"; }
        }
    }
    private ContentDialog? financialDialog;
    private object? dialogEvent;
    private bool purchaseDialog;
    private string? dialogSubject;
    private string? dialogProfile;
    private string? historyBinding;
    private bool renderingPurchases;
    private void OnPurchasePosChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!renderingPurchases) RenderPurchases();
    }
    private async Task<ContentDialogResult> ShowFinancialDialogAsync(ContentDialog dialog, bool purchase = false)
    {
        if (financialDialog is not null) return ContentDialogResult.None;
        dialog.RequestedTheme = RequestedTheme;
        financialDialog = dialog; dialogEvent = KnownEvents.SelectedItem; purchaseDialog = purchase;
        dialogSubject = connection.Subject; dialogProfile = connection.Profile?.Id;
        try { return await dialog.ShowAsync(); }
        finally { financialDialog = null; dialogSubject = null; dialogProfile = null; dialogEvent = null; purchaseDialog = false; }
    }
    private void RenderPurchases()
    {
        if (PurchaseFeedback is null) return;
        if (financialDialog is not null && (connection.Subject != dialogSubject || connection.Profile?.Id != dialogProfile || !Equals(dialogEvent, KnownEvents.SelectedItem) || (purchaseDialog && !connection.IsPurchaseReviewCurrent))) financialDialog.Hide();
        PurchaseFeedback.Text = connection.PurchaseStatus;
        renderingPurchases = true;
        var selected = PurchasePos.SelectedItem;
        PurchasePos.ItemsSource = connection.PointOfSales;
        PurchasePos.SelectedItem = selected;
        if (PurchasePos.SelectedItem is null && connection.PointOfSales.Count == 1) PurchasePos.SelectedIndex = 0;
        renderingPurchases = false;
        PurchaseContext.Text = connection.CashierIdentifier is { } cashier
            ? $"Cashier: {cashier}"
            : "Cashier access required.";
        ReviewPurchaseButton.IsEnabled = connection.CashierIdentifier is not null && connection.Composer.Response is not null &&
            !connection.MutationBusy && !connection.Busy;
        var binding = connection.Subject is null || PurchasePos.SelectedItem is not int ? null :
            string.Join("\0", connection.Subject, connection.Profile?.Id, connection.Profile?.Backend.OriginalString,
                connection.Profile?.Issuer.OriginalString, connection.Profile?.ClientId, KnownEvents.SelectedItem, PurchasePos.SelectedItem);
        if (binding != historyBinding)
        {
            historyBinding = binding;
            printWatches.Clear(); LivePurchaseHistory.Children.Clear();
            if (binding is not null) RefreshPurchaseHistory();
        }
        if (connection.Subject is null) PurchasePlayer.Text = "";
    }

    private async void OnReviewPurchase(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PurchasePos.SelectedItem is not int pos) throw new InvalidOperationException();
            var account = await connection.ReadPurchaseAccountAsync(PurchasePlayer.Text, pos);
            var review = connection.ReviewPurchase(PurchasePlayer.Text, pos);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Review purchase",
                Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = review.Summary + "\n" + account,
                    TextWrapping = TextWrapping.Wrap } }, PrimaryButtonText = "Purchase",
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowFinancialDialogAsync(dialog, true) == ContentDialogResult.Primary) await connection.ConfirmPurchaseAsync(review.ReviewId);
            else connection.CancelPurchaseReview();
            RenderPurchases();
            RefreshPurchaseHistory();
            refresh.Request();
        }
        catch (Exception error) { PurchaseFeedback.Text = $"Purchase unavailable or review expired ({error.GetType().Name}, 0x{error.HResult:X8}). Check verified player/POS, current quote and encrypted journal. No replacement intent will be sent automatically."; }
    }

    private void OnRefreshPurchaseHistory(object sender, RoutedEventArgs e) => RefreshPurchaseHistory();
    private void RefreshPurchaseHistory()
    {
        printWatches.Clear(); LivePurchaseHistory.Children.Clear();
        try
        {
            if (KnownEvents.SelectedItem is not Guid day || PurchasePos.SelectedItem is not int pos) return;

            var entries = connection.LocalPurchases(day.ToString(), pos);
            PurchaseFeedback.Text = connection.PurchaseStatus;
            foreach (var entry in entries)
            {
                LivePurchaseHistory.Children.Add(new TextBlock { Text = entry.DisplaySummary, TextWrapping = TextWrapping.Wrap });
                LivePurchaseHistory.Children.Add(new Expander { Header = "Details", HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBlock { Text = entry.Summary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } });
                if (entry.State == "ACKED")
                {
                    var status = new TextBlock { Text = "Checking print status…", TextWrapping = TextWrapping.Wrap };
                    Button? reprint = null;
                    var watch = new PrintWatch(entry.Id, day.ToString(), pos, status);
                    if (printWatches.Count < 10) printWatches.Add(watch);
                    LivePurchaseHistory.Children.Add(status);
                    if (entry.CanReprint)
                    {
                        reprint = new Button { Content = "Reprint", IsEnabled = connection.CanRequestReprint(entry.Id) };
                        reprint.Click += async (_, _) =>
                        {
                            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Duplicate output risk",
                                Content = "This creates another print job. Check the printer first to avoid duplicate output. The receipt does not mark REPRINT.",
                                PrimaryButtonText = "Request one reprint", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                            if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
                            try { await connection.ReprintAsync(entry.Id, day.ToString(), pos); RenderPurchases(); RefreshPurchaseHistory(); refresh.Request(); }
                            catch { PurchaseFeedback.Text = "Reprint unavailable or uncertain. Preserve and recover the original reprint intent before another request."; }
                        };
                        LivePurchaseHistory.Children.Add(reprint);
                    }
                    watch.Reprint = reprint;
                    refresh.Request();
                }
                if (entry.CanRecover)
                {
                    var recover = new Button { Content = "Recover original uncertain intent", IsEnabled = !connection.MutationBusy };
                    recover.Click += async (_, _) =>
                    {
                        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Replay original intent?",
                            Content = "Recovery can complete a previously uncommitted purchase and debit the account. It sends the exact original request and key as the same operator.",
                            PrimaryButtonText = "Recover original intent", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                        if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
                        try { await connection.RecoverPurchaseAsync(entry.Id, day.ToString(), pos); RenderPurchases(); RefreshPurchaseHistory(); }
                        catch { PurchaseFeedback.Text = "Recovery unavailable. Preserve the original intent and restore the same identity/context; do not create a replacement key."; }
                    };
                    LivePurchaseHistory.Children.Add(recover);
                }
            }
        }
        catch { PurchaseFeedback.Text = "History unavailable in this verified context, or encrypted journal requires investigation. Records have not been deleted."; }
    }
}
