using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

internal interface IAccountBackend
{
    Task<AccountInformation> GetAccountAsync(AccountQuery query, string token, CancellationToken cancellation);
}
internal interface IAdministrationBackend
{
    Task<byte[]> TransmitAdministrationAsync(AdministrationRecord saved, string token, CancellationToken cancellation);
    Task<EnrollmentInformation> GetEnrollmentAsync(EnrollmentQuery query, string token, CancellationToken cancellation);
    Task<PlayerList> ListPlayersAsync(DayQuery query, string token, CancellationToken cancellation);
}

public sealed partial class ReadOnlyBackend : IAdministrationBackend, IAccountBackend
{
    async Task<byte[]> IAdministrationBackend.TransmitAdministrationAsync(AdministrationRecord saved, string accessToken, CancellationToken cancellation)
    {
        if (saved.State != MutationState.Sending || saved.Binding.Profile != profile.Id ||
            saved.Binding.Backend != profile.Backend.OriginalString || saved.Binding.Issuer != profile.Issuer.OriginalString ||
            saved.Binding.Client != profile.ClientId || !Guid.TryParse(saved.Binding.Event, out var day) ||
            !profile.KnownEvents.Contains(day)) throw new InvalidOperationException("Administration origin mismatch.");
        AdministrationWire.Identity(saved.Method, saved.Request);
        var headers = new Metadata { { "authorization", "Bearer " + accessToken } };
        var until = DateTime.UtcNow.Add(deadline);
        IMessage response = saved.Method switch
        {
            AdministrationWire.Prefix + "RaceService/ScratchHorse" => await new RaceService.RaceServiceClient(channel)
                .ScratchHorseAsync(ScratchHorseRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceService/VoidRace" => await new RaceService.RaceServiceClient(channel)
                .VoidRaceAsync(RaceCommand.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "ResultService/SubmitResult" => await new ResultService.ResultServiceClient(channel)
                .SubmitResultAsync(SubmitResultRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "ResultService/VerifyResult" => await new ResultService.ResultServiceClient(channel)
                .VerifyResultAsync(VerifyResultRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "ResultService/SettleRace" => await new ResultService.ResultServiceClient(channel)
                .SettleRaceAsync(SettleRaceRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceDayService/CreateRaceDay" => await new RaceDayService.RaceDayServiceClient(channel)
                .CreateRaceDayAsync(CreateRaceDayRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceDayService/PublishRaceDay" => await new RaceDayService.RaceDayServiceClient(channel)
                .PublishRaceDayAsync(DayCommand.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceDayService/StartRaceDay" => await new RaceDayService.RaceDayServiceClient(channel)
                .StartRaceDayAsync(DayCommand.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceService/OpenBetting" => await new RaceService.RaceServiceClient(channel)
                .OpenBettingAsync(RaceCommand.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "RaceService/CloseBetting" => await new RaceService.RaceServiceClient(channel)
                .CloseBettingAsync(RaceCommand.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "EnrollmentService/EnrollPlayer" => await new EnrollmentService.EnrollmentServiceClient(channel)
                .EnrollPlayerAsync(EnrollPlayerRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            AdministrationWire.Prefix + "EnrollmentService/AddOpeningFunding" => await new EnrollmentService.EnrollmentServiceClient(channel)
                .AddOpeningFundingAsync(AddOpeningFundingRequest.Parser.ParseFrom(saved.Request), headers, until, cancellation),
            _ => throw new InvalidOperationException("Unsupported administration method.")
        };
        return response.ToByteArray();
    }
    Task<AccountInformation> IAccountBackend.GetAccountAsync(AccountQuery query, string token, CancellationToken cancellation) =>
        ReadAccountAsync(query, token, cancellation);

    async Task<EnrollmentInformation> IAdministrationBackend.GetEnrollmentAsync(EnrollmentQuery query, string accessToken, CancellationToken cancellation) =>
        await new EnrollmentService.EnrollmentServiceClient(channel).GetEnrollmentAsync(query,
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellation);
    async Task<PlayerList> IAdministrationBackend.ListPlayersAsync(DayQuery query, string accessToken, CancellationToken cancellation) =>
        await new EnrollmentService.EnrollmentServiceClient(channel).ListPlayersAsync(query,
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellation);
}
