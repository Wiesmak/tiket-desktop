using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class RefreshTests
{
    [Fact] public async Task CoalescesAndBoundsFrequency()
    {
        using var c = new RefreshCoordinator(); c.Select("actor/event", TimeSpan.Zero);
        int calls = 0;
        Task<bool> Read(CancellationToken _) { calls++; return Task.FromResult(true); }
        for (int second = 0; second <= 60; second++)
            await c.TickAsync(TimeSpan.FromSeconds(second), true, true, Read);
        Assert.Equal(7, calls);
        for (int i = 0; i < 100; i++) c.Request();
        await c.TickAsync(TimeSpan.FromSeconds(61), true, true, Read);
        Assert.Equal(7, calls);
        await c.TickAsync(TimeSpan.FromSeconds(62), true, true, Read);
        Assert.Equal(8, calls);
    }
    [Fact] public async Task BackoffAndPauseDoNotMultiplyRequests()
    {
        using var c = new RefreshCoordinator(); c.Select("a", TimeSpan.Zero);
        int calls = 0;
        Task<bool> Read(CancellationToken _) { calls++; return Task.FromResult(false); }
        for (int second = 0; second < 300; second++)
            await c.TickAsync(TimeSpan.FromSeconds(second), true, true, Read);
        Assert.InRange(calls, 4, 5);
        for (int second = 300; second < 900; second++)
            await c.TickAsync(TimeSpan.FromSeconds(second), false, true, Read);
        Assert.InRange(calls, 4, 5);
        c.Select(null, TimeSpan.FromSeconds(900));
        await c.TickAsync(TimeSpan.FromSeconds(901), true, true, Read);
        Assert.InRange(calls, 4, 5);
    }
    [Fact] public async Task OldContextIsCancelledAndCannotOverlapOrScheduleNewContext()
    {
        using var c = new RefreshCoordinator(); c.Select("old", TimeSpan.Zero);
        var reply = new TaskCompletionSource<bool>(); CancellationToken old = default;
        var pending = c.TickAsync(TimeSpan.Zero, true, true, token => { old = token; return reply.Task; });
        c.Select("new", TimeSpan.FromSeconds(2));
        Assert.True(old.IsCancellationRequested);
        int calls = 0;
        Task<bool> Read(CancellationToken _) { calls++; return Task.FromResult(true); }
        await c.TickAsync(TimeSpan.FromSeconds(2), true, true, Read);
        Assert.Equal(0, calls);
        reply.SetResult(false); await pending;
        await c.TickAsync(TimeSpan.FromSeconds(3), true, true, Read);
        Assert.Equal(1, calls);
    }
    [Fact] public async Task IdenticalSnapshotsRetainQuoteDraftAndDoNotNotifyEditor()
    {
        using var editor = QuoteTests.Editor();
        await editor.QuoteAsync((_, _) => Task.FromResult(QuoteTests.Reply()));
        var draft = editor.Draft; var time = editor.QuotedAt;
        int changes = 0; editor.Changed += () => changes++;
        for (int i = 0; i < 100; i++) editor.SynchronizeContext(QuoteTests.Day(), QuoteTests.Race());
        Assert.Equal(0, changes); Assert.Same(draft, editor.Draft);
        Assert.Equal(time, editor.QuotedAt); Assert.NotNull(editor.Response);
        var race = QuoteTests.Race(); race.Status.Version++;
        editor.SynchronizeContext(QuoteTests.Day(), race);
        Assert.Null(editor.Response); Assert.Same(draft, editor.Draft);
        Assert.Equal(QuoteState.Stale, editor.State);
    }
}
