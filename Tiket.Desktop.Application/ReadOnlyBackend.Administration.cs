using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

public sealed partial class ReadOnlyBackend
{
    internal async Task<byte[]> TransmitAdministrationAsync(AdministrationRecord saved, string accessToken, CancellationToken cancellation)
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
    internal async Task<EnrollmentInformation> GetEnrollmentAsync(EnrollmentQuery query, string accessToken, CancellationToken cancellation) =>
        await new EnrollmentService.EnrollmentServiceClient(channel).GetEnrollmentAsync(query,
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellation);
    internal async Task<PlayerList> ListPlayersAsync(DayQuery query, string accessToken, CancellationToken cancellation) =>
        await new EnrollmentService.EnrollmentServiceClient(channel).ListPlayersAsync(query,
            new Metadata { { "authorization", "Bearer " + accessToken } }, DateTime.UtcNow.Add(deadline), cancellation);
}
