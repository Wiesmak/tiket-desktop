namespace Tiket.Desktop.Application;

public sealed partial class ConnectionSession
{
    public async Task EnrollRosterAsync(IReadOnlyList<PlayerAlias> roster, Action<string> progress, CancellationToken cancellation)
    {
        if (selectedEvent is not { } day || roster.Count is < 1 or > 1000 || roster.Select(p => p.Player).Distinct().Count() != roster.Count)
            throw new InvalidOperationException("Select an event and a unique player roster.");
        var actor = generation;
        await ReadPlayersAsync(); // Authoritative enrollment, including restart/resume.
        var enrolled = EnrolledPlayers?.Players.Select(p => p.PlayerId).ToHashSet() ?? throw new InvalidOperationException("Full player list required.");
        foreach (var player in roster)
        {
            cancellation.ThrowIfCancellationRequested();
            if (actor != generation || selectedEvent != day) throw new OperationCanceledException();
            if (AdministrationHistory(day).Any(h => h.CanRecover)) throw new InvalidOperationException("Recover the original uncertain command before continuing enrollment.");
            var id = player.Player.ToString("D");
            if (!enrolled.Contains(id))
            {
                var pending = ReviewEnrollment(id, 0, 200, false);
                await ConfirmAdministrationAsync(pending.Id);
                if (AdministrationHistory(day).Any(h => h.CanRecover) || AdministrationStatus != "Completed")
                    throw new InvalidOperationException("Enrollment stopped: " + AdministrationStatus);
                enrolled.Add(id);
            }
            SelectPlayer(id);
            if (!await ReadSelectedPlayerAsync(cancellation) || CurrentAccount is not { } account || account.PlayerId != id)
                throw new InvalidOperationException("Enrollment account could not be confirmed.");
            progress($"{player.DisplayName} · {id} · authoritative balance {account.Balance} {account.Currency}\n{enrolled.Count} enrolled on server");
        }
        await ReadPlayersAsync();
    }
}
