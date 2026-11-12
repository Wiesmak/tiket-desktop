using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData(0, WorkspaceLayout.Stacked)]
    [InlineData(488, WorkspaceLayout.Stacked)]
    [InlineData(834, WorkspaceLayout.Stacked)]
    [InlineData(899.9, WorkspaceLayout.Stacked)]
    [InlineData(900, WorkspaceLayout.Columns)]
    [InlineData(1172, WorkspaceLayout.Columns)]
    [InlineData(double.NaN, WorkspaceLayout.Stacked)]
    public void LayoutUsesAvailableWorkspaceWidth(double width, WorkspaceLayout expected) =>
        Assert.Equal(expected, PreviewPresentation.LayoutFor(width));

    [Fact]
    public void FixtureStatusesCannotBeMistakenForOperationalJobs()
    {
        var summaries = Enum.GetValues<MockPrintState>().Select(PreviewPresentation.PrintSummary).ToArray();
        Assert.Equal(3, summaries.Distinct().Count());
        Assert.All(summaries, summary => Assert.StartsWith("MOCK · Sample", summary));
        Assert.Contains("pending", summaries[0]);
        Assert.Contains("paper delivery unconfirmed", summaries[1]);
        Assert.Contains("failed", summaries[2]);
        Assert.Contains("fictional", PreviewPresentation.MockBanner);
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPresentation.PrintSummary((MockPrintState)99));
    }

    [Fact]
    public void EveryBuilderStepExplainsItsLocalBoundary()
    {
        var descriptions = Enumerable.Range(0, 5).Select(PreviewPresentation.StepDescription).ToArray();
        Assert.Equal(5, descriptions.Distinct().Count());
        Assert.Contains("leading zeros", descriptions[2]);
        Assert.Contains("one-shot", descriptions[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPresentation.StepDescription(-1));
    }

    [Fact]
    public void AppearanceIsSessionOnlyAndRejectsUnknownValues()
    {
        var state = new AppearanceState();
        Assert.Equal(Appearance.System, state.Selected);
        foreach (var appearance in Enum.GetValues<Appearance>())
        {
            state.Select(appearance);
            Assert.Equal(appearance, state.Selected);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Select((Appearance)99));
        Assert.Equal(Appearance.System, new AppearanceState().Selected);
    }

    [Fact]
    public void RepeatedNavigationPreservesUnconnectedBoundary()
    {
        var state = new ShellState();
        for (var i = 0; i < 10; i++)
            foreach (var destination in Enum.GetValues<Destination>())
            {
                state.Navigate(destination);
                Assert.Equal(destination, state.Destination);
                Assert.False(state.CanSubmit);
                Assert.Contains("demonstration only", state.ConnectionStatus);
            }
    }
}
