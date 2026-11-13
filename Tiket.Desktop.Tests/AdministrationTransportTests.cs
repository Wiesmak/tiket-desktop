using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;
using Tiket.Desktop.Application;
using Xunit;

namespace Tiket.Desktop.Tests;

public class AdministrationTransportTests
{
    private static AdministrationRecord Intent(ConnectionProfile profile, AdministrationOperation operation)
    {
        var day = ConnectionTests.Day.ToString(); var key = Guid.NewGuid().ToString();
        IMessage request = operation switch {
            AdministrationOperation.CreateRaceDay => new CreateRaceDayRequest { Definition = AdministrationSessionTests.Definition(), IdempotencyKey = key },
            AdministrationOperation.PublishRaceDay or AdministrationOperation.StartRaceDay => new DayCommand { RaceDayId = day, ExpectedVersion = 7, IdempotencyKey = key },
            AdministrationOperation.OpenBetting or AdministrationOperation.CloseBetting => new RaceCommand { RaceDayId = day, RaceId = QuoteTests.RaceId, ExpectedDayVersion = 7, ExpectedRaceVersion = 13, IdempotencyKey = key },
            AdministrationOperation.EnrollPlayer => new EnrollPlayerRequest { RaceDayId = day, PlayerId = QuoteTests.Horses[0], OpeningPaid = 31, OpeningPromotional = 7, ExpectedDayVersion = 7, IdempotencyKey = key },
            _ => new AddOpeningFundingRequest { RaceDayId = day, PlayerId = QuoteTests.Horses[0], AdditionalPaid = 31, AdditionalPromotional = 7, ExpectedAccountVersion = 5, IdempotencyKey = key }
        };
        var service = operation is AdministrationOperation.CreateRaceDay or AdministrationOperation.PublishRaceDay or AdministrationOperation.StartRaceDay ?
            "RaceDayService/" : operation is AdministrationOperation.OpenBetting or AdministrationOperation.CloseBetting ? "RaceService/" : "EnrollmentService/";
        return new(1, Guid.NewGuid(), new(profile.Id,profile.Backend.OriginalString,profile.Issuer.OriginalString,profile.ClientId,"actor",day),
            AdministrationWire.Prefix+service+operation,request.ToByteArray(),key,DateTimeOffset.UtcNow,MutationState.Sending,null);
    }
    public static IEnumerable<object[]> Operations => Enum.GetValues<AdministrationOperation>().Where(op => op <= AdministrationOperation.AddOpeningFunding).Select(op=>new object[]{op});
    [Theory] [MemberData(nameof(Operations))]
    public async Task EveryGeneratedCommandSendsExactBytesOneBearerAndDeadline(AdministrationOperation operation)
    {
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start();
        var profile=ConnectionTests.Profile(fixture.Address,development:true);
        using var backend=new ReadOnlyBackend(profile);
        var intent=Intent(profile,operation);
        var bytes=await ((IAdministrationBackend)backend).TransmitAdministrationAsync(intent,"access-only",default);
        Assert.Equal("/"+intent.Method,fixture.AdministrationPath);Assert.Equal(intent.Request,fixture.AdministrationRequest);
        Assert.Equal(new[]{"Bearer access-only"},fixture.Calls.Single().Auth);Assert.Equal("HTTP/2",fixture.Calls.Single().Protocol);
        Assert.NotEmpty(fixture.Calls.Single().Timeout);AdministrationWire.Verify(intent,bytes);
        Assert.ThrowsAny<Exception>(()=>AdministrationWire.Verify(intent,new DayAcknowledgement { RaceDayId=Guid.NewGuid().ToString(),Phase=DayPhase.Ready }.ToByteArray()));
    }
    [Theory] [InlineData("enrollment")] [InlineData("players")] [InlineData("account")]
    public async Task ScopedGeneratedReadsUseCorrectMethodAndPayload(string read)
    {
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start();
        using var backend=new ReadOnlyBackend(ConnectionTests.Profile(fixture.Address,development:true));
        var day=ConnectionTests.Day.ToString();var player=QuoteTests.Horses[0];
        string method;byte[] expected;
        if(read=="enrollment") {
            var query=new EnrollmentQuery{RaceDayId=day,PlayerId=player};
            Assert.Equal(38,(await ((IAdministrationBackend)backend).GetEnrollmentAsync(query,"access-only",default)).Balance);
            method="EnrollmentService/GetEnrollment";expected=query.ToByteArray();
        } else if(read=="players") {
            var query=new DayQuery{RaceDayId=day};
            Assert.Single((await ((IAdministrationBackend)backend).ListPlayersAsync(query,"access-only",default)).Players);
            method="EnrollmentService/ListPlayers";expected=query.ToByteArray();
        } else {
            var query=new AccountQuery{RaceDayId=day,PlayerId=player};
            Assert.Equal(38,(await ((IAccountBackend)backend).GetAccountAsync(query,"access-only",default)).Balance);
            method="AccountService/GetAccount";expected=query.ToByteArray();
        }
        Assert.Equal("/"+AdministrationWire.Prefix+method,fixture.AdministrationPath);Assert.Equal(expected,fixture.AdministrationRequest);
        Assert.Equal(new[]{"Bearer access-only"},fixture.Calls.Single().Auth);
    }
    [Theory] [InlineData(StatusCode.PermissionDenied)] [InlineData(StatusCode.Unauthenticated)] [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.InvalidArgument)] [InlineData(StatusCode.AlreadyExists)] [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.Unavailable)] [InlineData(StatusCode.DeadlineExceeded)] [InlineData(StatusCode.NotFound)]
    public async Task ErrorsNeverAutomaticallyRetry(StatusCode code)
    {
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start(code);
        var profile=ConnectionTests.Profile(fixture.Address,development:true);
        using var backend=new ReadOnlyBackend(profile);
        var error=await Assert.ThrowsAsync<RpcException>(()=>((IAdministrationBackend)backend).TransmitAdministrationAsync(Intent(profile,AdministrationOperation.EnrollPlayer),"access-only",default));
        Assert.Equal(code,error.StatusCode);Assert.Single(fixture.Calls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task CancellationAndDeadlineReachRealTransport(bool cancel)
    {
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start(delay:true);
        var profile=ConnectionTests.Profile(fixture.Address,development:true);
        using var backend=new ReadOnlyBackend(profile,cancel?TimeSpan.FromSeconds(5):TimeSpan.FromMilliseconds(150));
        using var cancellation=new CancellationTokenSource();if(cancel)cancellation.CancelAfter(150);
        var error=await Assert.ThrowsAsync<RpcException>(()=>((IAdministrationBackend)backend).TransmitAdministrationAsync(Intent(profile,AdministrationOperation.CreateRaceDay),"access-only",cancellation.Token));
        Assert.Equal(cancel?StatusCode.Cancelled:StatusCode.DeadlineExceeded,error.StatusCode);Assert.InRange(fixture.Calls.Count,0,1);
    }
    [Fact] public async Task RedirectCannotForwardAdministrativeBearer()
    {
        await using var other=await GrpcTransportTests.GrpcFixture.Start();
        await using var fixture=await GrpcTransportTests.GrpcFixture.Start(redirect:other.Address);
        var profile=ConnectionTests.Profile(fixture.Address,development:true);using var backend=new ReadOnlyBackend(profile);
        await Assert.ThrowsAsync<RpcException>(()=>((IAdministrationBackend)backend).TransmitAdministrationAsync(Intent(profile,AdministrationOperation.AddOpeningFunding),"access-only",default));
        Assert.Single(fixture.Calls);Assert.Empty(other.Calls);
    }
}
