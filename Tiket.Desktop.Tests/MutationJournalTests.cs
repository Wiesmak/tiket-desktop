using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public sealed class MutationJournalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tiket-journal-tests", Guid.NewGuid().ToString("N"));
    private static MutationBinding Binding => new("test-profile", "https://api.invalid", "https://issuer.invalid",
        "native-test", "subject-sensitive-marker", Guid.NewGuid().ToString(), 7);
    private static MutationRecord Intent(MutationBinding? binding = null)
    {
        var b = binding ?? Binding;
        var request = new PurchaseCouponRequest
        {
            RaceDayId = b.Event, RaceId = Guid.NewGuid().ToString(), PlayerId = Guid.NewGuid().ToString(),
            CouponId = Guid.NewGuid().ToString(), IdempotencyKey = Guid.NewGuid().ToString(),
            PointOfSaleNumber = b.Pos, CashierIdentifier = "cashier-sensitive-marker", QuoteConfirmation = "opaque-confirmation"
        };
        return new(1, Guid.NewGuid(), b, "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon",
            request.ToByteArray(), request.CouponId, request.IdempotencyKey, request.PlayerId,
            request.CashierIdentifier, Encoding.UTF8.GetBytes("review-sensitive-marker"), DateTimeOffset.UtcNow,
            MutationState.Prepared, null, null);
    }

    [Fact]
    public void DpapiRoundTripRetainsExactRequestWithoutPlaintextAtRest()
    {
        var intent = Intent();
        using (var journal = new MutationJournal(directory)) journal.Prepare(intent);
        var raw = File.ReadAllBytes(Path.Combine(directory, "mutations.v1.dpapi"));
        var text = Encoding.UTF8.GetString(raw);
        foreach (var marker in new[] { intent.Binding.Subject, intent.Player, intent.Cashier, intent.Coupon, intent.Key,
                     "opaque-confirmation", "review-sensitive-marker", intent.Binding.Backend })
            Assert.DoesNotContain(marker, text);
        using var reopened = new MutationJournal(directory);
        var saved = Assert.Single(reopened.Read(intent.Binding));
        Assert.Equal(intent.Request, saved.Request);
        Assert.Equal(intent.ReviewedQuote, saved.ReviewedQuote);
        Assert.Equal(MutationState.Prepared, saved.State);
    }

    [Fact]
    public void CrashAfterSendingReopensUnknownAndPreservesReplayIdentity()
    {
        var intent = Intent();
        using (var journal = new MutationJournal(directory))
        {
            journal.Prepare(intent);
            journal.MarkSending(intent.Id, intent.Binding);
        }
        using (var restarted = new MutationJournal(directory))
        {
            var unknown = Assert.Single(restarted.Read(intent.Binding));
            Assert.Equal(MutationState.Unknown, unknown.State);
            Assert.Equal(intent.Request, unknown.Request);
            Assert.Equal(intent.Key, unknown.Key);
            restarted.MarkSending(intent.Id, intent.Binding);
            restarted.Acknowledge(intent.Id, intent.Binding, [1, 2, 3]);
        }
        using var final = new MutationJournal(directory);
        var ack = Assert.Single(final.Read(intent.Binding));
        Assert.Equal(MutationState.Acked, ack.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, ack.Response);
        Assert.Throws<InvalidOperationException>(() => final.MarkSending(intent.Id, intent.Binding));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("profile")]
    [InlineData("backend")]
    [InlineData("issuer")]
    [InlineData("client")]
    [InlineData("event")]
    [InlineData("pos")]
    public void EveryBindingDimensionIsRequiredForVisibilityAndRecovery(string field)
    {
        var intent = Intent();
        var b = intent.Binding;
        var other = field switch
        {
            "subject" => b with { Subject = "other" }, "profile" => b with { Profile = "other" },
            "backend" => b with { Backend = "https://other.invalid" }, "issuer" => b with { Issuer = "https://other.invalid" },
            "client" => b with { Client = "other" }, "event" => b with { Event = Guid.NewGuid().ToString() },
            _ => b with { Pos = 8 }
        };
        using var journal = new MutationJournal(directory);
        journal.Prepare(intent);
        Assert.Empty(journal.Read(other));
        Assert.Throws<InvalidOperationException>(() => journal.MarkSending(intent.Id, other));
        Assert.Equal(MutationState.Prepared, Assert.Single(journal.Read(b)).State);
    }

    [Fact]
    public void CompetingWriterCannotOpenJournal()
    {
        using var first = new MutationJournal(directory);
        Assert.Throws<IOException>(() => new MutationJournal(directory));
    }

    [Fact]
    public async Task SeparateProcessCannotAcquireWriterFile()
    {
        using var journal = new MutationJournal(directory);
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["TIKET_TEST_LOCK_PATH"] = Path.Combine(directory, "writer.lock");
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
                     "try { $s=[System.IO.File]::Open($env:TIKET_TEST_LOCK_PATH,[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); $s.Dispose(); exit 3 } catch { if ($_.Exception -is [System.IO.IOException] -or $_.Exception.InnerException -is [System.IO.IOException]) { exit 0 }; exit 4 }" }) start.ArgumentList.Add(arg);
        using var child = System.Diagnostics.Process.Start(start)!;
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, child.ExitCode);
    }

    [Fact]
    public void PersistedDataCannotBeEditedThroughInputOrReturnedArrays()
    {
        using var journal = new MutationJournal(directory);
        var intent = Intent();
        var original = intent.Request.ToArray();
        var prepared = journal.Prepare(intent);
        intent.Request[0] ^= 1;
        prepared.Request[0] ^= 1;
        Assert.Equal(original, Assert.Single(journal.Read(intent.Binding)).Request);
        var returned = Assert.Single(journal.Read(intent.Binding));
        returned.Request[0] ^= 1;
        Assert.Equal(original, Assert.Single(journal.Read(intent.Binding)).Request);
    }

    [Fact]
    public void DuplicateIdentityIsRejectedAndConcurrentClicksHaveOneWinner()
    {
        using var journal = new MutationJournal(directory);
        var intent = Intent();
        var successes = 0;
        Parallel.For(0, 8, _ =>
        {
            try { journal.Prepare(intent); Interlocked.Increment(ref successes); }
            catch (InvalidOperationException) { }
        });
        Assert.Equal(1, successes);
        Assert.Single(journal.Read(intent.Binding));
    }

    [Fact]
    public void CorruptionFailsClosedWithoutOverwritingData()
    {
        using (var journal = new MutationJournal(directory)) journal.Prepare(Intent());
        var path = Path.Combine(directory, "mutations.v1.dpapi");
        var corrupted = File.ReadAllBytes(path);
        corrupted[^1] ^= 255;
        File.WriteAllBytes(path, corrupted);
        Assert.Throws<CryptographicException>(() => new MutationJournal(directory));
        Assert.Equal(corrupted, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingExistingJournalCannotBeReinitialized()
    {
        using (var journal = new MutationJournal(directory)) journal.Prepare(Intent());
        File.Delete(Path.Combine(directory, "mutations.v1.dpapi"));
        Assert.Throws<IOException>(() => new MutationJournal(directory));
    }

    [Fact]
    public void CommitFailureQuarantinesAndPendingFileBlocksReopen()
    {
        using (var journal = new MutationJournal(directory)) { }
        using (var journal = new MutationJournal(directory, replace: (_, _) => throw new IOException("injected")))
        {
            Assert.Throws<IOException>(() => journal.Prepare(Intent()));
            Assert.Throws<IOException>(() => journal.Read(Binding));
        }
        Assert.Throws<IOException>(() => new MutationJournal(directory));
    }

    [Fact]
    public void EncryptionFailureCannotYieldPreparedRecord()
    {
        using (var journal = new MutationJournal(directory)) { }
        using var failed = new MutationJournal(directory, new FailingProtection());
        Assert.Throws<CryptographicException>(() => failed.Prepare(Intent()));
        Assert.Throws<IOException>(() => failed.Read(Binding));
    }

    [Fact]
    public async Task LostSyntheticResponseRecoversExactIntentAfterReopen()
    {
        var intent = Intent();
        var request = PurchaseCouponRequest.Parser.ParseFrom(intent.Request);
        var pricing = new CouponPricing { RaceDayId = request.RaceDayId, RaceId = request.RaceId,
            Currency = "TEST", TotalStake = 10 };
        intent = intent with { ReviewedQuote = new QuoteCouponResponse { Pricing = pricing }.ToByteArray() };
        var response = new PurchaseCouponResponse { CouponId = request.CouponId, PlayerId = request.PlayerId,
            Pricing = pricing, PrintJobId = Guid.NewGuid().ToString(), PrintingStatus = DispatchStatus.Pending }.ToByteArray();
        byte[]? first = null;
        using (var journal = new MutationJournal(directory))
        {
            journal.Prepare(intent);
            await Assert.ThrowsAsync<MutationUncertainException>(() => new JournaledMutation(journal).SendAsync(
                intent.Id, intent.Binding, (saved, _) =>
                {
                    first = saved.Request;
                    Assert.Equal(MutationState.Sending, Assert.Single(journal.Read(intent.Binding)).State);
                    throw new IOException("SYNTHETIC committed reply loss");
                }, CancellationToken.None));
            Assert.Equal(MutationState.Unknown, Assert.Single(journal.Read(intent.Binding)).State);
        }
        using var restarted = new MutationJournal(directory);
        var ack = await new JournaledMutation(restarted).SendAsync(intent.Id, intent.Binding, (saved, _) =>
        {
            Assert.Equal(first, saved.Request);
            Assert.Equal(intent.Key, saved.Key);
            return Task.FromResult(response);
        }, CancellationToken.None);
        Assert.Equal(MutationState.Acked, ack.State);
        Assert.Equal(response, ack.Response);
    }

    [Fact]
    public async Task CancellationAfterReceivedResponseStillRecordsAcknowledgement()
    {
        var intent = Intent();
        var request = PurchaseCouponRequest.Parser.ParseFrom(intent.Request);
        var pricing = new CouponPricing { Currency = "TEST" };
        intent = intent with { ReviewedQuote = new QuoteCouponResponse { Pricing = pricing }.ToByteArray() };
        using var journal = new MutationJournal(directory);
        journal.Prepare(intent);
        using var cancellation = new CancellationTokenSource();
        var ack = await new JournaledMutation(journal).SendAsync(intent.Id, intent.Binding, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new PurchaseCouponResponse { CouponId = request.CouponId, PlayerId = request.PlayerId,
                Pricing = pricing, PrintJobId = Guid.NewGuid().ToString(), PrintingStatus = DispatchStatus.Pending }.ToByteArray());
        }, cancellation.Token);
        Assert.Equal(MutationState.Acked, ack.State);
    }

    [Fact]
    public async Task MismatchedAcknowledgementStaysUnknown()
    {
        using var journal = new MutationJournal(directory);
        var intent = Intent();
        journal.Prepare(intent);
        await Assert.ThrowsAsync<MutationUncertainException>(() => new JournaledMutation(journal).SendAsync(
            intent.Id, intent.Binding, (_, _) => Task.FromResult(new PurchaseCouponResponse {
                CouponId = Guid.NewGuid().ToString() }.ToByteArray()), CancellationToken.None));
        Assert.Equal(MutationState.Unknown, Assert.Single(journal.Read(intent.Binding)).State);
    }

    [Fact]
    public async Task PreCancelledOperationNeverTransmitsOrMarksSending()
    {
        using var journal = new MutationJournal(directory);
        var intent = Intent();
        journal.Prepare(intent);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sent = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JournaledMutation(journal).SendAsync(
            intent.Id, intent.Binding, (_, _) => { sent = true; return Task.FromResult(new byte[] { 1 }); }, cancellation.Token));
        Assert.False(sent);
        Assert.Equal(MutationState.Prepared, Assert.Single(journal.Read(intent.Binding)).State);
    }

    [Fact]
    public async Task SendingCommitFailureCannotInvokeNetwork()
    {
        using (var empty = new MutationJournal(directory)) { }
        var commits = 0;
        using var journal = new MutationJournal(directory, replace: (source, target) =>
        {
            if (++commits == 2) throw new IOException("injected SENDING commit failure");
            File.Move(source, target, true);
        });
        var intent = Intent(); journal.Prepare(intent);
        var transmissions = 0;
        await Assert.ThrowsAsync<IOException>(() => new JournaledMutation(journal).SendAsync(intent.Id, intent.Binding,
            (_, _) => { transmissions++; return Task.FromResult(new byte[] { 1 }); }, CancellationToken.None));
        Assert.Equal(0, transmissions);
    }

    [Fact]
    public async Task ReceivedAckPersistenceFailureRetainsSendingAndQuarantines()
    {
        using (var empty = new MutationJournal(directory)) { }
        var commits = 0;
        using (var journal = new MutationJournal(directory, replace: (source, target) =>
               {
                   if (++commits == 3) throw new IOException("injected ACK commit failure");
                   File.Move(source, target, true);
               }))
        {
            var intent = Intent(); var request = PurchaseCouponRequest.Parser.ParseFrom(intent.Request);
            var pricing = new CouponPricing { Currency = "TEST" };
            intent = intent with { ReviewedQuote = new QuoteCouponResponse { Pricing = pricing }.ToByteArray() };
            journal.Prepare(intent);
            await Assert.ThrowsAsync<MutationUncertainException>(() => new JournaledMutation(journal).SendAsync(intent.Id, intent.Binding,
                (_, _) => Task.FromResult(new PurchaseCouponResponse { CouponId = request.CouponId, PlayerId = request.PlayerId,
                    Pricing = pricing, PrintJobId = Guid.NewGuid().ToString(), PrintingStatus = DispatchStatus.Pending }.ToByteArray()), CancellationToken.None));
            Assert.Throws<IOException>(() => journal.Read(intent.Binding));
            using var snapshot = System.Text.Json.JsonDocument.Parse(new CurrentUserJournalProtection().Unprotect(
                File.ReadAllBytes(Path.Combine(directory, "mutations.v1.dpapi"))));
            Assert.Equal((int)MutationState.Sending, snapshot.RootElement.GetProperty("Records")[0].GetProperty("State").GetInt32());
        }
        Assert.True(File.Exists(Path.Combine(directory, "mutations.v1.dpapi.pending")));
        Assert.Throws<IOException>(() => new MutationJournal(directory));
    }

    [Fact]
    public void UnknownSchemaCannotBeSilentlyMigratedOrOverwritten()
    {
        using (var empty = new MutationJournal(directory)) { }
        var bytes = new CurrentUserJournalProtection().Protect(Encoding.UTF8.GetBytes("{\"Schema\":999,\"Records\":[]}"));
        var path = Path.Combine(directory, "mutations.v1.dpapi"); File.WriteAllBytes(path, bytes);
        Assert.Throws<IOException>(() => new MutationJournal(directory));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void SnapshotQuotaFailurePreservesCommittedDataAndQuarantines()
    {
        using var journal = new MutationJournal(directory);
        var path = Path.Combine(directory, "mutations.v1.dpapi");
        var committed = File.ReadAllBytes(path);
        var oversized = Intent() with { ReviewedQuote = new byte[8 * 1024 * 1024] };
        Assert.Throws<IOException>(() => journal.Prepare(oversized));
        Assert.Equal(committed, File.ReadAllBytes(path));
        Assert.Throws<IOException>(() => journal.Read(oversized.Binding));
        Assert.False(File.Exists(path + ".pending"));
    }

    private sealed class FailingProtection : IJournalProtection
    {
        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("injected");
        public byte[] Unprotect(byte[] ciphertext) => new CurrentUserJournalProtection().Unprotect(ciphertext);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
