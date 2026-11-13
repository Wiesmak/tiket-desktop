using Microsoft.UI.Xaml;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;
public sealed partial class MainPage
{
    private EventPayoutReport? eventReport;
    private string? reportContext;
    private async void OnBuildEventReport(object sender, RoutedEventArgs e)
    {
        try
        {
            eventReport = null; ExportEventReportButton.IsEnabled = BuildEventReportButton.IsEnabled = false;
            var (profile, day) = AliasContext(); reportContext = profile + "|" + day + "|" + connection.Subject;
            eventReport = await connection.BuildEventReportAsync(Aliases.Load(profile, day), message => EventReportDetails.Text = message, default);
            EventReportDetails.Text = eventReport.Display(); ExportEventReportButton.IsEnabled = true;
        }
        catch (Exception error) { EventReportDetails.Text = "Report unavailable: " + error.Message; }
        finally { BuildEventReportButton.IsEnabled = true; }
    }
    private async void OnExportEventReport(object sender, RoutedEventArgs e)
    {
        try
        {
            if (eventReport is null || reportContext != connection.Profile?.Id + "|" + KnownEvents.SelectedItem + "|" + connection.Subject || !connection.IsAuditor)
                throw new InvalidOperationException("Build the displayed report for this operator and event first.");
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "Tiket-" + eventReport.Event };
            picker.FileTypeChoices.Add("CSV report", new[] { ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(((App)Microsoft.UI.Xaml.Application.Current).MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) await Windows.Storage.FileIO.WriteTextAsync(file, eventReport.Csv());
        }
        catch (Exception error) { EventReportDetails.Text += "\nExport: " + error.Message; }
    }
}
