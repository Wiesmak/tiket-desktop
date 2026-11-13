using System.Collections.Immutable;
using System.Globalization;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public enum SelectionMode { Normal, Wall, Box }
public sealed record SlotChoice(string? HorseId = null, bool IsWall = false);
public sealed record QuoteDraft(PoolType Pool, SelectionMode Mode, ImmutableArray<SlotChoice> Slots,
    ImmutableArray<string> Box, string Units)
{
    public static QuoteDraft Empty => new(PoolType.Win, SelectionMode.Normal, [new()], [], "1");
}
public enum QuoteState { Editing, Invalid, Quoting, Quoted, Stale, Denied, Offline, Cancelled, Expired, UnknownEvent, Limited, Error }

/// <summary>Dispatcher-owned editor. No expansion, price arithmetic, persistence or purchase capability.</summary>
public sealed class CashierComposer : IDisposable
{
    private CancellationTokenSource? pending;
    private long revision;
    private bool disposed;
    private RaceDayInformation? day;
    private RaceInformation? race;
    public QuoteDraft Draft { get; private set; } = QuoteDraft.Empty;
    public QuoteState State { get; private set; } = QuoteState.Editing;
    private QuoteCouponResponse? response;
    // Return a copy so callers cannot mutate the original authoritative response retained in memory.
    public QuoteCouponResponse? Response => response?.Clone();
    public DateTimeOffset? QuotedAt { get; private set; }
    public string Message { get; private set; } = "Select an authorized server race in Settings.";
    public event Action? Changed;
    public static int Arity(PoolType pool) => pool switch { PoolType.Win => 1, PoolType.Quinella or PoolType.Exacta => 2, PoolType.Trifecta => 3, _ => 0 };
    public static bool TryUnits(string text, out long units)
    {
        units = 0;
        return text.Length is > 0 and <= 19 && text.All(c => c is >= '0' and <= '9') &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out units) && units > 0;
    }
    public void SetContext(RaceDayInformation? newDay, RaceInformation? newRace)
    {
        if (disposed) return;
        day = newDay?.Clone(); race = newRace?.Clone();
        Invalidate();
    }
    public void SynchronizeContext(RaceDayInformation? newDay, RaceInformation? newRace)
    {
        if (Equals(day, newDay) && Equals(race, newRace)) return;
        var wasQuoted = response is not null;
        SetContext(newDay, newRace);
        if (wasQuoted) { State = QuoteState.Stale; Message = "Race changed — check selection and get a new quote."; Changed?.Invoke(); }
    }
    public void Edit(QuoteDraft draft) { Draft = draft; Invalidate(); }
    public void Cancel() { Invalidate(); State = QuoteState.Cancelled; Message = "Quote cancelled."; Changed?.Invoke(); }
    private void Invalidate()
    {
        revision++;
        pending?.Cancel(); pending?.Dispose(); pending = null;
        response = null; QuotedAt = null;
        Message = Validation() ?? "Ready for quote.";
        State = Validation() is null ? QuoteState.Editing : QuoteState.Invalid;
        Changed?.Invoke();
    }
    public string? Validation()
    {
        if (day?.Definition is null || race?.Definition is null || day.Status is null || race.Status is null)
            return "Select an authorized server race in Settings.";
        if (day.Status.Phase != DayPhase.Running || race.Status.Phase != RacePhase.BettingOpen)
            return day.Status.Phase != DayPhase.Running ? "Day is not running." : "Betting is not open.";
        if (!Guid.TryParse(day.Definition.RaceDayId, out _) || !Guid.TryParse(race.Definition.RaceId, out _))
            return "Server context is incomplete.";
        int arity = Arity(Draft.Pool);
        if (arity == 0 || !race.Definition.Pools.Any(p => p.Type == Draft.Pool)) return "Select an available pool.";
        if (!TryUnits(Draft.Units, out _)) return "Enter whole units from 1 to 9223372036854775807 using digits 0–9.";
        IEnumerable<string> explicitIds;
        if (Draft.Mode == SelectionMode.Box)
        {
            if (Draft.Box.Length < Math.Max(2, arity)) return "Box needs at least " + Math.Max(2, arity) + " horses.";
            explicitIds = Draft.Box;
        }
        else
        {
            if (Draft.Mode is not (SelectionMode.Normal or SelectionMode.Wall)) return "Select a supported mode.";
            if (Draft.Slots.Length != arity) return "Complete every position.";
            if (Draft.Slots.Any(s => s.IsWall && s.HorseId is not null)) return "A position must contain one choice.";
            int walls = Draft.Slots.Count(s => s.IsWall);
            if (Draft.Mode == SelectionMode.Normal && walls != 0) return "Normal requires explicit horses.";
            if (Draft.Mode == SelectionMode.Wall && (arity == 1 || walls == 0 || walls == arity))
                return "Wall requires both WALL and at least one explicit horse; WIN has no Wall mode.";
            if (Draft.Slots.Any(s => !s.IsWall && string.IsNullOrEmpty(s.HorseId))) return "Complete every position.";
            explicitIds = Draft.Slots.Where(s => !s.IsWall).Select(s => s.HorseId!);
        }
        var ids = explicitIds.ToArray();
        if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length) return "Choose each explicit horse only once.";
        if (ids.Any(id => !Guid.TryParse(id, out _) || !race.Definition.Runners.Any(h => h.HorseId == id))) return "Selection contains an unknown horse.";
        if (ids.Any(id => race.Status.ScratchedHorseIds.Contains(id))) return "A selected horse is scratched. Change the selection explicitly.";
        return null;
    }
    public QuoteCouponRequest Request()
    {
        if (Validation() is { } error) throw new InvalidOperationException(error);
        var selection = new BettingSelection();
        if (Draft.Mode == SelectionMode.Box) selection.Box = new BoxSelection { HorseIds = { Draft.Box } };
        else selection.Positional = new PositionalSelection { Positions = { Draft.Slots.Select(s => s.IsWall ? new Position { Wall = true } : new Position { HorseId = s.HorseId }) } };
        TryUnits(Draft.Units, out var units);
        return new QuoteCouponRequest { RaceDayId = day!.Definition.RaceDayId, RaceId = race!.Definition.RaceId,
            BetType = Draft.Pool, Selection = selection, UnitsPerCombination = units };
    }
    public async Task QuoteAsync(Func<QuoteCouponRequest, CancellationToken, Task<QuoteCouponResponse>> send)
    {
        if (State == QuoteState.Quoting || Validation() is not null) return;
        var request = Request(); // Unique immutable operation owns this protobuf; no UI alias.
        var current = ++revision;
        pending = new CancellationTokenSource();
        var token = pending.Token;
        response = null; QuotedAt = null; State = QuoteState.Quoting; Message = "Requesting server quote…"; Changed?.Invoke();
        try
        {
            var result = await send(request, token);
            if (current != revision || token.IsCancellationRequested) return;
            response = result.Clone(); QuotedAt = DateTimeOffset.Now; State = QuoteState.Quoted;
            Message = "Ready for review.";
        }
        catch (RpcException e) { if (current == revision) { State = Map(e.StatusCode); Message = ErrorMessage(State); } }
        catch (OperationCanceledException) { if (current == revision) { State = QuoteState.Cancelled; Message = ErrorMessage(State); } }
        catch (Exception) { if (current == revision) { State = QuoteState.Error; Message = ErrorMessage(State); } }
        finally { if (current == revision) { pending?.Dispose(); pending = null; Changed?.Invoke(); } }
    }
    public static QuoteState Map(StatusCode code) => code switch {
        StatusCode.Unauthenticated => QuoteState.Expired, StatusCode.PermissionDenied => QuoteState.Denied,
        StatusCode.InvalidArgument => QuoteState.Invalid, StatusCode.FailedPrecondition or StatusCode.Aborted => QuoteState.Stale,
        StatusCode.ResourceExhausted => QuoteState.Limited, StatusCode.NotFound => QuoteState.UnknownEvent,
        StatusCode.Unavailable or StatusCode.DeadlineExceeded => QuoteState.Offline, StatusCode.Cancelled => QuoteState.Cancelled, _ => QuoteState.Error };
    public static string ErrorMessage(QuoteState state) => state switch {
        QuoteState.Expired => "Session expired. Sign in again.", QuoteState.Denied => "Quotation denied. Check server role and event grants.",
        QuoteState.Invalid => "Server rejected the selection or units.", QuoteState.Stale => "Race changed — check selection and get a new quote.",
        QuoteState.Limited => "Server combination or request limit reached. Reduce selection or try later.",
        QuoteState.UnknownEvent => "Authorized event or race not found.", QuoteState.Offline => "Network unavailable or deadline exceeded.",
        QuoteState.Cancelled => "Quote cancelled.", _ => "Quotation failed. No wager was placed." };
    public static string RunnerLabel(Runner r, bool scratched = false) => r.ProgramNumber + " · " + r.DisplayName + (scratched ? " · SCRATCHED" : "");
    public static string Canonical(BettingSelection? s, IEnumerable<Runner> runners)
    {
        string Horse(string id) => runners.FirstOrDefault(r => r.HorseId == id) is { } h ? RunnerLabel(h) : id;
        return s?.VariantCase switch {
            BettingSelection.VariantOneofCase.Box => "Box: " + string.Join(", ", s.Box.HorseIds.Select(Horse)),
            BettingSelection.VariantOneofCase.Positional => string.Join(" / ", s.Positional.Positions.Select(p => p.ChoiceCase == Position.ChoiceOneofCase.Wall && p.Wall ? "WALL" : p.ChoiceCase == Position.ChoiceOneofCase.HorseId ? Horse(p.HorseId) : "Unknown choice")),
            _ => "Unavailable canonical selection" };
    }
    public void Dispose() { if (disposed) return; disposed = true; day = null; race = null; Invalidate(); Changed = null; }
}
