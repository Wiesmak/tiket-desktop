using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Tiket.Desktop.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Tiket_Desktop
{
    /// <summary>
    /// The main content page displayed inside the application window.
    /// Add your UI logic, event handlers, and data binding here.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public ShellState State { get; } = new();
        public MainPage()
        {
            InitializeComponent();
            OnBuilderStepChanged(this, null!);

            Navigation.SelectedItem = CashierItem;
            SizeChanged += (_, args) =>
            {
                var compact = args.NewSize.Width < 1000;
                Grid.SetColumn(RecentPanel, compact ? 0 : 1);
                Grid.SetRow(RecentPanel, compact ? 1 : 0);
                CashierGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 280);
            };
        }

        private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var destination = args.IsSettingsSelected ? Destination.Connection :
                Enum.TryParse<Destination>((args.SelectedItem as NavigationViewItem)?.Tag?.ToString(), out var value)
                    ? value : Destination.Cashier;
            State.Navigate(destination);
            CashierPanel.Visibility = destination == Destination.Cashier ? Visibility.Visible : Visibility.Collapsed;
            AdminPanel.Visibility = destination == Destination.Administration ? Visibility.Visible : Visibility.Collapsed;
            ConnectionPanel.Visibility = destination == Destination.Connection ? Visibility.Visible : Visibility.Collapsed;
            var heading = destination switch
            {
                Destination.Administration => AdminHeading,
                Destination.Connection => ConnectionHeading,
                _ => CashierHeading
            };
            heading.Focus(FocusState.Programmatic);
        }

        private void OnBuilderStepChanged(object sender, SelectionChangedEventArgs args)
        {
            if (StepDescription is null) return;
            StepDescription.Text = BuilderStep.SelectedIndex switch
            {
                0 => "Basics: date, currency and event time zone will be reviewed locally.",
                1 => "Races: order and titles will be prepared locally.",
                2 => "Runners: program numbers and names are shown below.",
                3 => "Pool rules: all four pools will require backend-compatible configuration.",
                _ => "Review: the complete definition must be reviewed before a future one-shot creation."
            };
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs args)
        {
            RequestedTheme = ((ComboBox)sender).SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }
}
