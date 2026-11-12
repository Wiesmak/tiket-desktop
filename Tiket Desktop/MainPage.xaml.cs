using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using Tiket.Desktop.Application;

namespace Tiket_Desktop;

public sealed partial class MainPage : Page
{
    public ShellState State { get; } = new();
    public AppearanceState AppearancePreference { get; } = new();
    private bool focusAfterPaneClose;
    private readonly UISettings uiSettings = new();
    private Storyboard? perspectiveTransition;

    public MainPage()
    {
        InitializeComponent();
        OnBuilderStepChanged(this, null!);
        Navigation.SelectedItem = CashierItem;
        InitializeConnection();
    }

    // Only measure the view here. All layout setters live in XAML, using the
    // workspace width after the native pane, scrollbar and padding take space.
    private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs args) =>
        VisualStateManager.GoToState(this, PreviewPresentation.LayoutFor(args.NewSize.Width).ToString(), false);

    private void OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        if (WorkspaceGutter is null) return;
        // The native minimal-mode menu occupies its own top band; content follows it.
        WorkspaceGutter.Padding = args.DisplayMode == NavigationViewDisplayMode.Minimal
            ? new Thickness(12, 48, 12, 24) : new Thickness(24, 16, 24, 24);
    }

    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var destination = args.IsSettingsSelected ? Destination.Connection :
            Enum.TryParse<Destination>((args.SelectedItem as NavigationViewItem)?.Tag?.ToString(), out var value)
                ? value : Destination.Cashier;
        var destinationChanged = State.Destination != destination;
        State.Navigate(destination);
        CashierPanel.Visibility = destination == Destination.Cashier ? Visibility.Visible : Visibility.Collapsed;
        AdminPanel.Visibility = destination == Destination.Administration ? Visibility.Visible : Visibility.Collapsed;
        ConnectionPanel.Visibility = destination == Destination.Connection ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceHeading.Text = destination switch
        {
            Destination.Administration => "Administration",
            Destination.Connection => "Settings / Connection",
            _ => "Cashier"
        };
        ContentScroll.ChangeView(null, 0, null, true);
        if (destinationChanged) AnimatePerspective(Workspace);
        if (IsLoaded && sender.DisplayMode != NavigationViewDisplayMode.Expanded && sender.IsPaneOpen)
        {
            focusAfterPaneClose = true;
            sender.IsPaneOpen = false;
        }
        else FocusWorkspace();
    }

    // Animate existing controls only; visibility, input values and focus remain independent.
    private void AnimatePerspective(FrameworkElement target)
    {
        perspectiveTransition?.Stop();
        perspectiveTransition = null;
        if (!IsLoaded || !uiSettings.AnimationsEnabled) return;

        var entrance = new Storyboard();
        var fade = new FadeInThemeAnimation();
        var reposition = new RepositionThemeAnimation { FromVerticalOffset = 16 };
        Storyboard.SetTarget(fade, target);
        Storyboard.SetTarget(reposition, target);
        entrance.Children.Add(fade);
        entrance.Children.Add(reposition);
        perspectiveTransition = entrance;
        entrance.Begin();
    }

    private void FocusWorkspace() => DispatcherQueue.TryEnqueue(() => WorkspaceHeading.Focus(FocusState.Programmatic));

    private void OnPaneClosed(NavigationView sender, object args)
    {
        if (!focusAfterPaneClose) return; // Let native dismissal restore its menu focus.
        focusAfterPaneClose = false;
        FocusWorkspace();
    }

    private void OnBuilderStepChanged(object sender, SelectionChangedEventArgs args)
    {
        if (StepDescription is null || BuilderStep.SelectedIndex < 0) return;
        var step = BuilderStep.SelectedIndex;
        StepDescription.Text = PreviewPresentation.StepDescription(step);
        BasicsStep.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        RacesStep.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        RunnersStep.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PoolsStep.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        ReviewStep.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        AnimatePerspective(step switch
        {
            0 => BasicsStep,
            1 => RacesStep,
            2 => RunnersStep,
            3 => PoolsStep,
            _ => ReviewStep
        });
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        var index = ((ComboBox)sender).SelectedIndex;
        if (index < 0) return;
        AppearancePreference.Select((Appearance)index);
        RequestedTheme = AppearancePreference.Selected switch
        {
            Appearance.Light => ElementTheme.Light,
            Appearance.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        // Keep focus on the native appearance control; no navigation or reconstruction.
    }
}
