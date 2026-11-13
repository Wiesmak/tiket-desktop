using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

// Explicit administration allow-list; no generic RPC invocation.
internal static class AdministrationWire
{
    internal const string Prefix = "pl.umamusume.tiket.api.v1.";
    internal static (string Day, string Key) Identity(string method, byte[] bytes)
    {
        IMessage request;
        (string Day, string Key) identity;
        switch (method)
        {
            case Prefix + "RaceDayService/CreateRaceDay":
                var create = CreateRaceDayRequest.Parser.ParseFrom(bytes); request = create;
                identity = (create.Definition?.RaceDayId ?? "", create.IdempotencyKey); break;
            case Prefix + "RaceDayService/PublishRaceDay":
            case Prefix + "RaceDayService/StartRaceDay":
                var day = DayCommand.Parser.ParseFrom(bytes); request = day;
                identity = (day.RaceDayId, day.IdempotencyKey); break;
            case Prefix + "RaceService/OpenBetting":
            case Prefix + "RaceService/CloseBetting":
            case Prefix + "RaceService/VoidRace":
                var race = RaceCommand.Parser.ParseFrom(bytes); request = race;
                identity = (race.RaceDayId, race.IdempotencyKey); break;
            case Prefix + "RaceService/ScratchHorse":
            case Prefix + "ResultService/SubmitResult":
            case Prefix + "ResultService/VerifyResult":
            case Prefix + "ResultService/SettleRace":
                request = Phase6Wire.Parse(method, bytes);
                var command = Phase6Wire.Command(request);
                identity = (command.RaceDayId, command.IdempotencyKey); break;
            case Prefix + "EnrollmentService/EnrollPlayer":
                var enroll = EnrollPlayerRequest.Parser.ParseFrom(bytes); request = enroll;
                identity = (enroll.RaceDayId, enroll.IdempotencyKey); break;
            case Prefix + "EnrollmentService/AddOpeningFunding":
                var funding = AddOpeningFundingRequest.Parser.ParseFrom(bytes); request = funding;
                identity = (funding.RaceDayId, funding.IdempotencyKey); break;
            default: throw new IOException("Unsupported administration method.");
        }
        if (!request.ToByteArray().AsSpan().SequenceEqual(bytes) ||
            !Guid.TryParseExact(identity.Day, "D", out var id) || id == Guid.Empty ||
            string.IsNullOrWhiteSpace(identity.Key)) throw new IOException("Invalid original administration payload.");
        return identity;
    }
    internal static void Verify(AdministrationRecord record, byte[] bytes)
    {
        if (Phase6Wire.IsMutation(record.Method)) { Phase6Wire.Verify(record, bytes); return; }
        if (record.Method is Prefix + "RaceDayService/CreateRaceDay" or Prefix + "RaceDayService/PublishRaceDay" or Prefix + "RaceDayService/StartRaceDay")
        {
            var ack = DayAcknowledgement.Parser.ParseFrom(bytes);
            var create = record.Method.EndsWith("/CreateRaceDay", StringComparison.Ordinal);
            var expectedPhase = create ? DayPhase.Draft : record.Method.EndsWith("/PublishRaceDay", StringComparison.Ordinal) ? DayPhase.Ready : DayPhase.Running;
            var expectedVersion = create ? 0 : checked(DayCommand.Parser.ParseFrom(record.Request).ExpectedVersion + 1);
            if (ack.RaceDayId != record.Binding.Event || ack.Version != expectedVersion || ack.Phase != expectedPhase)
                throw new IOException("Day acknowledgement mismatch.");
        }
        else if (record.Method is Prefix + "RaceService/OpenBetting" or Prefix + "RaceService/CloseBetting")
        {
            var request = RaceCommand.Parser.ParseFrom(record.Request);
            var ack = RaceAcknowledgement.Parser.ParseFrom(bytes);
            if (ack.RaceDayId != record.Binding.Event || ack.RaceId != request.RaceId || ack.Version != checked(request.ExpectedRaceVersion + 1) ||
                ack.Phase != (record.Method.EndsWith("/OpenBetting", StringComparison.Ordinal) ? RacePhase.BettingOpen : RacePhase.BettingClosed))
                throw new IOException("Race acknowledgement mismatch.");
        }
        else
        {
            var player = record.Method == Prefix + "EnrollmentService/EnrollPlayer" ?
                EnrollPlayerRequest.Parser.ParseFrom(record.Request).PlayerId : AddOpeningFundingRequest.Parser.ParseFrom(record.Request).PlayerId;
            var ack = EnrollmentInformation.Parser.ParseFrom(bytes);
            var enrollment = record.Method.EndsWith("/EnrollPlayer", StringComparison.Ordinal);
            var invalidAmounts = ack.OpeningPaid < 0 || ack.OpeningPromotional < 0 || ack.Balance < 0 || ack.OpeningVersion < 0;
            if (enrollment)
            {
                var request = EnrollPlayerRequest.Parser.ParseFrom(record.Request);
                invalidAmounts |= ack.OpeningPaid != request.OpeningPaid || ack.OpeningPromotional != request.OpeningPromotional;
            }
            else
            {
                var request = AddOpeningFundingRequest.Parser.ParseFrom(record.Request);
                invalidAmounts |= ack.OpeningPaid < request.AdditionalPaid || ack.OpeningPromotional < request.AdditionalPromotional ||
                    ack.AccountVersion != checked(request.ExpectedAccountVersion + 1);
            }
            if (ack.RaceDayId != record.Binding.Event || ack.PlayerId != player || ack.AccountVersion < 0 || invalidAmounts)
                throw new IOException("Enrollment acknowledgement mismatch.");
        }
    }
}
