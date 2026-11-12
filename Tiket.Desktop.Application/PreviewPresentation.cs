namespace Tiket.Desktop.Application;

public enum WorkspaceLayout { Stacked, Columns }
public enum Appearance { System, Light, Dark }
public enum MockPrintState { Pending, Dispatched, Failed }

/// <summary>Pure presentation policy, measured after navigation and workspace padding.</summary>
public static class PreviewPresentation
{
    public const double ColumnsMinimumWidth = 900;
    public static WorkspaceLayout LayoutFor(double contentWidth) =>
        double.IsFinite(contentWidth) && contentWidth >= ColumnsMinimumWidth
            ? WorkspaceLayout.Columns : WorkspaceLayout.Stacked;

    public const string MockBanner = "Transaction previews are fictional. Live reads are labelled SERVER. No purchases or printing.";
    public static string PrintSummary(MockPrintState state) => state switch
    {
        MockPrintState.Pending => "MOCK · Sample 001 · Print pending example",
        MockPrintState.Dispatched => "MOCK · Sample 002 · Dispatched example — paper delivery unconfirmed",
        MockPrintState.Failed => "MOCK · Sample 003 · Print failed example — no real job exists",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static string StepDescription(int step) => step switch
    {
        0 => "Basics · Fictional event name, date and time zone. Nothing is loaded from a server.",
        1 => "Races · Prepare a local order and title example. This is not a backend DRAFT editor.",
        2 => "Runners · Temporary roster text. Program numbers are strings; preserve leading zeros.",
        3 => "Pool rules · Preview the four supported pools. Prices and validation remain server authority.",
        4 => "Review · A future complete local definition must be reviewed before one-shot creation.",
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };
}

/// <summary>Session-only preference: a fresh process always follows the system.</summary>
public sealed class AppearanceState
{
    public Appearance Selected { get; private set; } = Appearance.System;
    public void Select(Appearance value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Selected = value;
    }
}
