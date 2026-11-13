using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public sealed class PurchaseSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tiket-session-tests", Guid.NewGuid().ToString("N"));
    private static readonly string Player = Guid.NewGuid().ToString();
    private static MutationBinding Binding => new("test", "https://api.example.invalid", "https://identity.example.invalid", "desktop",
        "fixture-subject", ConnectionTests.Day.ToString(), 7);
    private async Task<ConnectionSession> Session(MutationJournal journal, bool grants = true)
    {
        var session = new ConnectionSession(new Login(grants), _ => new Backend(), journal);
        session.Select(ConnectionTests.Profile());
        await session.SignInAsync();
        session.Composer.SetContext(QuoteTests.Day(), QuoteTests.Race());
        session.Composer.Edit(QuoteDraft.Empty with { Slots = [new(QuoteTests.Horses[0])] });
        var quote = QuoteTests.Reply();
        quote.Pricing.RaceDayId = ConnectionTests.Day.ToString(); quote.Pricing.RaceId = QuoteTests.RaceId;
        await session.Composer.QuoteAsync((_, _) => Task.FromResult(quote));
        return session;
    }

    [Fact]
    public async Task ReviewIsMemoryOnlyCancelCreatesNoIntent()
    {
        using var journal = new MutationJournal(directory);
        using var session = await Session(journal);
        var review = session.ReviewPurchase(Player, 7);
        Assert.Contains("fixture-cashier", review.Summary);
        Assert.Contains("1173 PLN", review.Summary);
        Assert.Empty(journal.Read(Binding));
        session.CancelPurchaseReview();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConfirmPurchaseAsync(review.ReviewId));
        Assert.Empty(journal.Read(Binding));
    }

    [Theory] [InlineData("signout")] [InlineData("quote-edit")] [InlineData("profile")]
    public async Task ReviewCannotSurviveIdentityProfileOrQuoteChange(string change)
    {
        using var journal = new MutationJournal(directory);
        using var session = await Session(journal);
        var review = session.ReviewPurchase(Player, 7);
        if (change == "signout") session.SignOut();
        if (change == "profile") session.Select(ConnectionTests.Profile("https://other.invalid"));
        if (change == "quote-edit") session.Composer.Edit(session.Composer.Draft with { Units = "2" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConfirmPurchaseAsync(review.ReviewId));
        Assert.Empty(journal.Read(Binding));
    }

    [Theory] [InlineData("player")] [InlineData("pos")] [InlineData("missing-grants")]
    public async Task UntrustedOperatingContextCannotPrepare(string failure)
    {
        using var journal = new MutationJournal(directory);
        using var session = await Session(journal, failure != "missing-grants");
        Assert.Throws<InvalidOperationException>(() => session.ReviewPurchase(failure == "player" ? Guid.NewGuid().ToString() : Player,
            failure == "pos" ? 8 : 7));
        Assert.Empty(journal.Read(Binding));
    }

    [Fact]
    public async Task FailedJournalCannotReachTransport()
    {
        using var journal = new MutationJournal(directory);
        using var session = await Session(journal);
        var review = session.ReviewPurchase(Player, 7);
        journal.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ConfirmPurchaseAsync(review.ReviewId));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InFlightSignOutOrOperatorSwitchRetainsOriginalSubjectOutcome(bool switchOperator)
    {
        await using var fixture = await GrpcTransportTests.GrpcFixture.Start();
        fixture.PurchaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var journal = new MutationJournal(directory);
        var login = new SwitchingLogin();
        using var session = new ConnectionSession(login, p => new ReadOnlyBackend(p), journal);
        session.Select(profile); await session.SignInAsync();
        session.Composer.SetContext(QuoteTests.Day(), QuoteTests.Race());
        session.Composer.Edit(QuoteDraft.Empty with { Slots = [new(QuoteTests.Horses[0])] });
        var quote = QuoteTests.Reply(); quote.Pricing.RaceDayId = ConnectionTests.Day.ToString(); quote.Pricing.RaceId = QuoteTests.RaceId;
        await session.Composer.QuoteAsync((_, _) => Task.FromResult(quote));
        var review = session.ReviewPurchase(Player, 7);
        var sending = session.ConfirmPurchaseAsync(review.ReviewId);
        await fixture.PurchaseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConfirmPurchaseAsync(review.ReviewId));
        session.SignOut();
        if (switchOperator) { login.Subject = "different-operator"; await session.SignInAsync(); }
        fixture.PurchaseGate.TrySetResult(true);
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        var oldBinding = Binding with { Backend = profile.Backend.OriginalString };
        var retained = Assert.Single(journal.Read(oldBinding));
        Assert.Equal(MutationState.Unknown, retained.State);
        Assert.Single(fixture.Calls);
        Assert.DoesNotContain("ACKED", session.PurchaseStatus);
        Assert.DoesNotContain(retained.Coupon, session.PurchaseStatus);
        if (switchOperator) Assert.Empty(session.LocalPurchases(ConnectionTests.Day.ToString(), 7));
        else Assert.Throws<InvalidOperationException>(() => session.LocalPurchases(ConnectionTests.Day.ToString(), 7));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ReducedPlayerOrChangedCashierGrantsHideAndBlockOriginalIntent(bool cashierChanged)
    {
        using var journal = new MutationJournal(directory);
        var login = new SwitchingLogin();
        using var session = new ConnectionSession(login, _ => new Backend(), journal);
        session.Select(ConnectionTests.Profile()); await session.SignInAsync();
        session.Composer.SetContext(QuoteTests.Day(), QuoteTests.Race());
        session.Composer.Edit(QuoteDraft.Empty with { Slots = [new(QuoteTests.Horses[0])] });
        var quote = QuoteTests.Reply(); quote.Pricing.RaceDayId = ConnectionTests.Day.ToString(); quote.Pricing.RaceId = QuoteTests.RaceId;
        await session.Composer.QuoteAsync((_, _) => Task.FromResult(quote));
        var review = session.ReviewPurchase(Player, 7);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConfirmPurchaseAsync(review.ReviewId));
        var retained = Assert.Single(journal.Read(Binding));
        Assert.Equal(MutationState.Prepared, retained.State);
        session.SignOut();
        if (cashierChanged) login.Cashier = "changed-cashier"; else login.Players = [];
        await session.SignInAsync();
        Assert.Empty(session.LocalPurchases(ConnectionTests.Day.ToString(), 7));
        Assert.Contains("grants", session.PurchaseStatus);
        Assert.DoesNotContain(retained.Coupon, session.PurchaseStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverPurchaseAsync(retained.Id, ConnectionTests.Day.ToString(), 7));
        Assert.Equal(MutationState.Prepared, Assert.Single(journal.Read(Binding)).State);
    }

    [Fact]
    public async Task EventSwitchCancelsPurchaseAndCannotDisplayOldOutcomeInNewContext()
    {
        await using var fixture = await GrpcTransportTests.GrpcFixture.Start();
        fixture.PurchaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = ConnectionTests.Profile(fixture.Address, development: true);
        using var journal = new MutationJournal(directory);
        using var session = new ConnectionSession(new SwitchingLogin(), p => new ReadOnlyBackend(p), journal);
        session.Select(profile); await session.SignInAsync(); session.SelectEvent(ConnectionTests.Day);
        session.Composer.SetContext(QuoteTests.Day(), QuoteTests.Race());
        session.Composer.Edit(QuoteDraft.Empty with { Slots = [new(QuoteTests.Horses[0])] });
        var quote = QuoteTests.Reply(); quote.Pricing.RaceDayId = ConnectionTests.Day.ToString(); quote.Pricing.RaceId = QuoteTests.RaceId;
        await session.Composer.QuoteAsync((_, _) => Task.FromResult(quote));
        var review = session.ReviewPurchase(Player, 7);
        var sending = session.ConfirmPurchaseAsync(review.ReviewId);
        await fixture.PurchaseReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.SelectEvent(Guid.NewGuid()); fixture.PurchaseGate.TrySetResult(true);
        await sending;
        var retained = Assert.Single(journal.Read(Binding with { Backend = profile.Backend.OriginalString }));
        Assert.Equal(MutationState.Unknown, retained.State);
        Assert.Equal("Ready", session.PurchaseStatus);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecoverPurchaseAsync(retained.Id, ConnectionTests.Day.ToString(), 7));
        Assert.Single(fixture.Calls);
    }

    private sealed class SwitchingLogin : INativeLogin
    {
        internal string Subject = "fixture-subject";
        internal string Cashier = "fixture-cashier";
        internal string[] Players = [Player];
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) =>
            Task.FromResult(new LoginMaterial(Subject, "synthetic-access", DateTimeOffset.UtcNow.AddMinutes(5))
            { OperatingGrants = new(Subject, Cashier, [ConnectionTests.Day.ToString()], Players, [7]) });
    }

    private sealed class Login(bool grants) : INativeLogin
    {
        public Task<LoginMaterial> SignInAsync(ConnectionProfile p, Action<ConnectionState> progress, CancellationToken ct) =>
            Task.FromResult(new LoginMaterial("fixture-subject", "synthetic-access", DateTimeOffset.UtcNow.AddMinutes(5))
            { OperatingGrants = grants ? new("fixture-subject", "fixture-cashier", [ConnectionTests.Day.ToString()], [Player], [7]) : null });
    }
    private sealed class Backend : IReadOnlyBackend
    {
        public Task<PingResponse> PingAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<EventOverview> ReadAsync(Guid day, string accessToken, CancellationToken ct) => throw new NotSupportedException();
        public void Dispose() { }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
