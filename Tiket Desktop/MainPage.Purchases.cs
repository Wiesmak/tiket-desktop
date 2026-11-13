using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tiket_Desktop;

public sealed partial class MainPage
{
    private ContentDialog? financialDialog;
    private string? dialogSubject;
    private string? dialogProfile;
    private string? historyBinding;
    private bool renderingPurchases;
    private void OnPurchasePosChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!renderingPurchases) RenderPurchases();
    }
    private async Task<ContentDialogResult> ShowFinancialDialogAsync(ContentDialog dialog)
    {
        if (financialDialog is not null) return ContentDialogResult.None;
        dialog.RequestedTheme = RequestedTheme;
        financialDialog = dialog;
        dialogSubject = connection.Subject; dialogProfile = connection.Profile?.Id;
        try { return await dialog.ShowAsync(); }
        finally { financialDialog = null; dialogSubject = null; dialogProfile = null; }
    }
    private void RenderPurchases()
    {
        if (PurchaseFeedback is null) return;
        if (financialDialog is not null && (connection.Subject != dialogSubject || connection.Profile?.Id != dialogProfile)) financialDialog.Hide();
        PurchaseFeedback.Text = connection.PurchaseStatus;
        renderingPurchases = true;
        var selected = PurchasePos.SelectedItem;
        PurchasePos.ItemsSource = connection.PointOfSales;
        PurchasePos.SelectedItem = selected;
        if (PurchasePos.SelectedItem is null && connection.PointOfSales.Count == 1) PurchasePos.SelectedIndex = 0;
        renderingPurchases = false;
        PurchaseContext.Text = connection.CashierIdentifier is { } cashier
            ? $"Verified cashier claim: {cashier}. Local choices do not grant authorization."
            : "Purchase unavailable: no verified cashier/POS access-token binding.";
        ReviewPurchaseButton.IsEnabled = connection.CashierIdentifier is not null && connection.Composer.Response is not null &&
            !connection.MutationBusy && !connection.Busy;
        var binding = connection.Subject is null || PurchasePos.SelectedItem is not int ? null :
            string.Join("\0", connection.Subject, connection.Profile?.Id, connection.Profile?.Backend.OriginalString,
                connection.Profile?.Issuer.OriginalString, connection.Profile?.ClientId, KnownEvents.SelectedItem, PurchasePos.SelectedItem);
        if (binding != historyBinding)
        {
            historyBinding = binding;
            LivePurchaseHistory.Children.Clear();
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
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Review actual purchase debit",
                Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = review.Summary + "\n" + account,
                    TextWrapping = TextWrapping.Wrap } }, PrimaryButtonText = "Confirm actual debit",
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await ShowFinancialDialogAsync(dialog) == ContentDialogResult.Primary) await connection.ConfirmPurchaseAsync(review.ReviewId);
            else connection.CancelPurchaseReview();
            RenderPurchases();
            RefreshPurchaseHistory();
        }
        catch (Exception error) { PurchaseFeedback.Text = $"Purchase unavailable or review expired ({error.GetType().Name}, 0x{error.HResult:X8}). Check verified player/POS, current quote and encrypted journal. No replacement intent will be sent automatically."; }
    }

    private void OnRefreshPurchaseHistory(object sender, RoutedEventArgs e) => RefreshPurchaseHistory();
    private void RefreshPurchaseHistory()
    {
        LivePurchaseHistory.Children.Clear();
        try
        {
            if (KnownEvents.SelectedItem is not Guid day || PurchasePos.SelectedItem is not int pos) return;
            LivePurchaseHistory.Children.Add(new TextBlock { Text = "LIVE · locally journal-known purchases only; not POS-wide history.", TextWrapping = TextWrapping.Wrap });
            var entries = connection.LocalPurchases(day.ToString(), pos);
            PurchaseFeedback.Text = connection.PurchaseStatus;
            foreach (var entry in entries)
            {
                LivePurchaseHistory.Children.Add(new TextBlock { Text = entry.Summary, TextWrapping = TextWrapping.Wrap });
                if (entry.State == "ACKED")
                {
                    var status = new TextBlock { Text = "Original acknowledgement status is historical; refresh for current backend state.", TextWrapping = TextWrapping.Wrap };
                    Button? reprint = null;
                    var refresh = new Button { Content = "Refresh current print status" };
                    refresh.Click += async (_, _) =>
                    {
                        try { status.Text = await connection.RefreshPrintAsync(entry.Id, day.ToString(), pos); if (reprint is not null) reprint.IsEnabled = connection.CanRequestReprint(entry.Id); }
                        catch { status.Text = "Print status unavailable in this current context. No reprint was requested."; }
                    };
                    LivePurchaseHistory.Children.Add(refresh); LivePurchaseHistory.Children.Add(status);
                    if (entry.CanReprint)
                    {
                        reprint = new Button { Content = "Request deliberate reprint", IsEnabled = connection.CanRequestReprint(entry.Id) };
                        reprint.Click += async (_, _) =>
                        {
                            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Duplicate output risk",
                                Content = "Inspect the original job first. Reprint creates another output job without a new wager or debit. Backend retries may already duplicate output. The serial receipt does not visibly mark REPRINT. Console output is not paper confirmation.",
                                PrimaryButtonText = "Request one reprint", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                            if (await ShowFinancialDialogAsync(dialog) != ContentDialogResult.Primary) return;
                            try { await connection.ReprintAsync(entry.Id, day.ToString(), pos); RenderPurchases(); RefreshPurchaseHistory(); }
                            catch { PurchaseFeedback.Text = "Reprint unavailable or uncertain. Preserve and recover the original reprint intent before another request."; }
                        };
                        LivePurchaseHistory.Children.Add(reprint);
                    }
                    if (entry.Id == entries[0].Id) _ = MonitorLatestPrintAsync(entry.Id, day.ToString(), pos, status, reprint);
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
    private async Task MonitorLatestPrintAsync(Guid id, string day, int pos, TextBlock status, Button? reprint)
    {
        try { await connection.MonitorPrintAsync(id, day, pos, value => { status.Text = value; if (reprint is not null) reprint.IsEnabled = connection.CanRequestReprint(id); }); }
        catch { /* Manual refresh remains available; no automatic reprint on read failure. */ }
    }
}
