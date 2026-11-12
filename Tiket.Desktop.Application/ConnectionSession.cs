using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

/// <summary>Owned by the UI dispatcher. Every operation uses a generation and cancellation scope.</summary>
public sealed class ConnectionSession : IDisposable
{
    private readonly INativeLogin login;
    private readonly Func<ConnectionProfile, IReadOnlyBackend> backendFactory;
    private CancellationTokenSource lifetime = new();
    private IReadOnlyBackend? backend;
    private LoginMaterial? material;
    private long generation;
    private bool disposed;
    public ConnectionProfile? Profile { get; private set; }
    public ConnectionState State { get; private set; } = ConnectionState.Unconfigured;
    public string? Subject => material?.Subject;
    public DateTimeOffset? ExpiresAt => material?.ExpiresAt;
    public EventOverview? Overview { get; private set; }
    public string? PingStatus { get; private set; }
    public bool Busy { get; private set; }
    public CashierComposer Composer { get; } = new();
    public event Action? Changed;

    public ConnectionSession(INativeLogin? login = null, Func<ConnectionProfile, IReadOnlyBackend>? backendFactory = null)
    {
        this.login = login ?? new NativeOidcLogin();
        this.backendFactory = backendFactory ?? (p => new ReadOnlyBackend(p));
    }

    public void Select(ConnectionProfile profile)
    {
        Clear();
        Profile = profile;
        State = ConnectionState.SignedOut;
        Changed?.Invoke();
    }

    private void Clear()
    {
        Composer.SetContext(null, null);
        generation++;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        backend?.Dispose();
        backend = null;
        material = null;
        Overview = null;
        PingStatus = null;
        Busy = false;
    }

    public void SignOut()
    {
        Clear();
        State = Profile is null ? ConnectionState.Unconfigured : ConnectionState.SignedOut;
        Changed?.Invoke();
    }
    public void Cancel()
    {
        if (Busy) SignOut();
        State = Profile is null ? ConnectionState.Unconfigured : ConnectionState.Cancelled;
        Changed?.Invoke();
    }
    public void CheckExpiry()
    {
        if (material is not null && material.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Clear();
            State = ConnectionState.Expired;
            Changed?.Invoke();
        }
    }

    public async Task SignInAsync()
    {
        if (Profile is null || Busy) return;
        Clear(); // Operator switch invalidates reads before discovery starts.
        var current = generation;
        Busy = true;
        try
        {
            var value = await login.SignInAsync(Profile, s =>
            {
                if (generation == current) { State = s; Changed?.Invoke(); }
            }, lifetime.Token);
            if (generation != current) return;
            material = value;
            State = ConnectionState.SignedIn;
        }
        catch (OperationCanceledException) { if (generation == current) State = ConnectionState.Cancelled; }
        catch (SignInFailure error) { if (generation == current) State = error.State; }
        catch (Exception) { if (generation == current) State = ConnectionState.Error; }
        finally { if (generation == current) { Busy = false; Changed?.Invoke(); } }
    }

    public async Task PingAsync()
    {
        if (Profile is null || Busy) return;
        await ExecuteAsync(async (service, token) =>
        {
            var ping = await service.PingAsync(token);
            return (object)$"Anonymous API ping: {ping.Status} · API {ping.ApiVersion} · {DateTimeOffset.Now:g}. Not database readiness or event authorization.";
        }, false);
    }

    public async Task ReadAsync(Guid day)
    {
        CheckExpiry();
        if (Profile is null || material is null || Busy || !Profile.KnownEvents.Contains(day)) return;
        Composer.SetContext(null, null);
        Overview = null;
        State = ConnectionState.Connecting;
        var credential = material.AccessToken;
        await ExecuteAsync(async (service, token) => (object)await service.ReadAsync(day, credential, token), true);
    }


    public Task QuoteAsync() => Composer.QuoteAsync(async (request, cancellation) =>
    {
        CheckExpiry();
        if (Profile is null || material is null || Overview is null || Busy || disposed ||
            request.RaceDayId != Overview.Day.Definition.RaceDayId ||
            !Overview.Races.Races.Any(r => r.Definition.RaceId == request.RaceId))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ""));
        var current = generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        try
        {
            backend ??= backendFactory(Profile);
            var result = await backend.QuoteAsync(request, material.AccessToken, linked.Token);
            CheckExpiry();
            if (current != generation || linked.IsCancellationRequested) throw new OperationCanceledException();
            return result;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unauthenticated && current == generation)
        {
            Clear(); State = ConnectionState.Expired; Changed?.Invoke(); throw;
        }
    });

    private async Task ExecuteAsync(Func<IReadOnlyBackend, CancellationToken, Task<object>> operation, bool secured)
    {
        var current = generation;
        Busy = true;
        Changed?.Invoke();
        try
        {
            backend ??= backendFactory(Profile!);
            var result = await operation(backend, lifetime.Token);
            if (current != generation) return;
            if (secured)
            {
                CheckExpiry();
                if (current != generation) return;
                Overview = (EventOverview)result;
                State = ConnectionState.Ready;
            }
            else PingStatus = (string)result;
        }
        catch (RpcException error)
        {
            if (current != generation) return;
            var state = ConnectionErrors.State(error.StatusCode);
            if (secured) Overview = null;
            if (state == ConnectionState.Expired) Clear();
            State = state;
        }
        catch (OperationCanceledException) { if (current == generation) State = ConnectionState.Cancelled; }
        catch (Exception) { if (current == generation) State = ConnectionState.Error; }
        finally
        {
            if (current == generation) Busy = false;
            Changed?.Invoke();
        }
    }

    public void Dispose() { if (disposed) return; disposed = true; Clear(); lifetime.Dispose(); Changed = null; }
}
