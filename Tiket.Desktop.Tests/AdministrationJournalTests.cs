using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class AdministrationJournalTests
{
    private static AdministrationRecord Record() {
        var day = Guid.NewGuid().ToString(); var key = Guid.NewGuid().ToString();
        return new(1, Guid.NewGuid(), new("fixture", "https://backend.invalid", "https://issuer.invalid", "client", "actor", day),
            AdministrationWire.Prefix + "RaceDayService/PublishRaceDay",
            new DayCommand { RaceDayId = day, ExpectedVersion = 1, IdempotencyKey = key }.ToByteArray(),
            key, DateTimeOffset.UtcNow, MutationState.Prepared, null);
    }
    [Fact] public async Task DurableUnknownReopensAndReplaysExactRequestOnlyAsOriginalActor()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var record = Record(); byte[] original = record.Request.ToArray();
        try {
            using (var journal = new AdministrationJournal(dir)) {
                journal.Prepare(record); record.Request[0] ^= 1;
                var saved = journal.Read(record.Binding).Single(); Assert.Equal(original, saved.Request);
                await Assert.ThrowsAsync<MutationUncertainException>(() => new JournaledAdministration(journal)
                    .SendAsync(record.Id, record.Binding, (_, _) => throw new IOException("lost response"), default));
            }
            using (var reopened = new AdministrationJournal(dir)) {
                Assert.Equal(MutationState.Unknown, reopened.Read(record.Binding).Single().State);
                Assert.Empty(reopened.Read(record.Binding with { Subject = "other" }));
                int calls = 0;
                var result = await new JournaledAdministration(reopened).SendAsync(record.Id, record.Binding, (r, _) => {
                    calls++; Assert.Equal(original, r.Request); Assert.Equal(record.Key, r.Key);
                    return Task.FromResult(new DayAcknowledgement { RaceDayId = r.Binding.Event, Phase = DayPhase.Ready, Version = 2 }.ToByteArray());
                }, default);
                Assert.Equal(1, calls); Assert.Equal(MutationState.Acked, result.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() => new JournaledAdministration(reopened)
                    .SendAsync(record.Id, record.Binding, (_, _) => throw new Exception(), default));
            }
        } finally { Directory.Delete(dir, true); }
    }
    [Fact] public void CrashSendingBecomesUnknownAndMultiwriterIsRejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var r = Record();
        try {
            using (var journal = new AdministrationJournal(dir)) {
                journal.Prepare(r); journal.MarkSending(r.Id, r.Binding);
                Assert.Throws<IOException>(() => new AdministrationJournal(dir));
            }
            using var reopened = new AdministrationJournal(dir);
            Assert.Equal(MutationState.Unknown, reopened.Read(r.Binding).Single().State);
        } finally { Directory.Delete(dir, true); }
    }
    [Fact] public async Task FailedDurableSendCommitDispatchesNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var r = Record();
        int replacements = 0, calls = 0;
        try {
            using var journal = new AdministrationJournal(dir, replace: (source, target) => {
                if (++replacements == 3) throw new IOException("disk failed");
                File.Move(source, target, true);
            });
            journal.Prepare(r);
            await Assert.ThrowsAsync<IOException>(() => new JournaledAdministration(journal).SendAsync(r.Id, r.Binding,
                (_, _) => { calls++; return Task.FromResult(Array.Empty<byte>()); }, default));
            Assert.Equal(0, calls);
        } finally { Directory.Delete(dir, true); }
    }
    [Fact] public void SchemaAndMethodAreIndependentOfPurchaseJournal()
    {
        var r = Record();
        Assert.Throws<IOException>(() => AdministrationWire.Identity("pl.umamusume.tiket.api.v1.RaceDayService/CloseRaceDay", r.Request));
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            using (var journal = new AdministrationJournal(dir)) journal.Prepare(r);
            var bytes = File.ReadAllBytes(Path.Combine(dir, "administration.v1.dpapi"));
            Assert.DoesNotContain("actor", System.Text.Encoding.UTF8.GetString(bytes));
            Assert.False(File.Exists(Path.Combine(dir, "mutations.v1.dpapi")));
        } finally { Directory.Delete(dir, true); }
    }
}
