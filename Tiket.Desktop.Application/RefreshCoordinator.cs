namespace Tiket.Desktop.Application;

/// <summary>Dispatcher-owned read scheduler. The caller supplies a monotonic elapsed time and drives TickAsync.
/// Context changes cancel old reads; a late old completion cannot change scheduling for the new context.
/// No tokens, mutation callbacks or automatic command recovery belong here.</summary>
public sealed class RefreshCoordinator : IDisposable
{
    private CancellationTokenSource context = new();
    private string? key;
    private long generation;
    private bool running, disposed, requested;
    private TimeSpan due, lastStarted = TimeSpan.MinValue;
    private int failures;
    public static readonly TimeSpan MinimumSeparation = TimeSpan.FromSeconds(2);
    public bool InFlight => running;
    public void Select(string? value, TimeSpan now)
    {
        if (disposed || key == value) return;
        context.Cancel(); context.Dispose(); context = new();
        key = value; generation++; failures = 0; requested = true; due = now;
    }
    public void Request() { if (!disposed && key is not null) requested = true; }
    public async Task TickAsync(TimeSpan now, bool foreground, bool activeCashier,
        Func<CancellationToken, Task<bool>> read)
    {
        if (disposed || key is null || !foreground || running ||
            (lastStarted != TimeSpan.MinValue && now - lastStarted < MinimumSeparation) ||
            (!requested && now < due)) return;
        var current = generation;
        var cancellation = context.Token;
        running = true; requested = false; lastStarted = now;
        try
        {
            var success = await read(cancellation);
            if (current != generation || cancellation.IsCancellationRequested) return;
            failures = success ? 0 : Math.Min(failures + 1, 4);
            // Bounded deterministic jitter avoids identical retry cadence; no random state in tests.
            var seconds = failures == 0 ? (activeCashier ? 10 : 30) :
                Math.Min(120, 10 * (1 << failures)) + failures;
            due = now + TimeSpan.FromSeconds(seconds);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch
        {
            if (current == generation) { failures = Math.Min(failures + 1, 4); due = now + TimeSpan.FromSeconds(Math.Min(120, 10 * (1 << failures)) + failures); }
        }
        finally { running = false; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; context.Cancel(); context.Dispose(); key = null;
    }
}
