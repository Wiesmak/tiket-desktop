using Google.Protobuf;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

internal static class Phase6Wire
{
    internal static bool IsMutation(string method) => method is
        AdministrationWire.Prefix + "RaceService/ScratchHorse" or AdministrationWire.Prefix + "RaceService/VoidRace" or
        AdministrationWire.Prefix + "ResultService/SubmitResult" or AdministrationWire.Prefix + "ResultService/VerifyResult" or
        AdministrationWire.Prefix + "ResultService/SettleRace";
    internal static string RequiredRole(string method) => method switch {
        AdministrationWire.Prefix + "ResultService/SubmitResult" => "RESULT_SUBMITTER",
        AdministrationWire.Prefix + "ResultService/VerifyResult" => "RESULT_VERIFIER",
        AdministrationWire.Prefix + "ResultService/SettleRace" => "SETTLEMENT_OPERATOR", _ => "EVENT_ADMIN"
    };
    internal static IMessage Parse(string method, byte[] bytes) => method switch {
        AdministrationWire.Prefix + "RaceService/ScratchHorse" => ScratchHorseRequest.Parser.ParseFrom(bytes),
        AdministrationWire.Prefix + "RaceService/VoidRace" => RaceCommand.Parser.ParseFrom(bytes),
        AdministrationWire.Prefix + "ResultService/SubmitResult" => SubmitResultRequest.Parser.ParseFrom(bytes),
        AdministrationWire.Prefix + "ResultService/VerifyResult" => VerifyResultRequest.Parser.ParseFrom(bytes),
        AdministrationWire.Prefix + "ResultService/SettleRace" => SettleRaceRequest.Parser.ParseFrom(bytes),
        _ => throw new IOException("Unsupported result mutation.")
    };
    internal static RaceCommand Command(IMessage message) => message switch {
        RaceCommand c => c, ScratchHorseRequest r => r.Command, SubmitResultRequest r => r.Command,
        VerifyResultRequest r => r.Command, SettleRaceRequest r => r.Command,
        _ => throw new IOException("Missing race command.")
    } ?? throw new IOException("Missing race command.");
    internal static void Verify(AdministrationRecord record, byte[] bytes)
    {
        var request = Parse(record.Method, record.Request);
        var command = Command(request);
        RaceAcknowledgement status;
        if (request is ScratchHorseRequest || request is RaceCommand) {
            var refund = RefundAcknowledgement.Parser.ParseFrom(bytes);
            status = refund.Status;
            if (refund.RefundedLegs < 0 || refund.RefundedStake < 0 || refund.Players.Any(p => p.Amount < 0) ||
                refund.Pools.Any(p => p.RefundedStake < 0 || p.RefundedUnits < 0))
                throw new IOException("Invalid refund summary.");
            if (request is ScratchHorseRequest scratch && !status.ScratchedHorseIds.Contains(scratch.HorseId))
                throw new IOException("Scratch acknowledgement mismatch.");
            if (request is RaceCommand && status.Phase != RacePhase.Voided) throw new IOException("Void acknowledgement mismatch.");
        } else if (request is SettleRaceRequest settle) {
            var ack = SettlementInformation.Parser.ParseFrom(bytes); status = ack.Status;
            if (ack.SettlementId != settle.SettlementId || status.Phase != RacePhase.Settled ||
                ack.ResultRevision != status.SelectedResultRevision || string.IsNullOrWhiteSpace(ack.ResultRevision) ||
                ack.Pools.Any(p => p.ValidStake < 0 || p.WinningUnits < 0 || p.Distributed < 0 || p.HouseAllocation < 0) ||
                ack.Players.Any(p => p.Amount < 0)) throw new IOException("Settlement acknowledgement mismatch.");
        } else {
            var ack = ResultAcknowledgement.Parser.ParseFrom(bytes); status = ack.Status;
            var revision = request is SubmitResultRequest submit ? submit.Revision : ((VerifyResultRequest)request).Revision;
            if (ack.Result is null || ack.Result.RaceDayId != command.RaceDayId || ack.Result.RaceId != command.RaceId ||
                ack.Result.Revision != revision || status.SelectedResultRevision != revision ||
                ack.Result.Status != (request is SubmitResultRequest ? OfficialResultStatus.Proposed : OfficialResultStatus.Verified) ||
                status.Phase != (request is SubmitResultRequest ? RacePhase.ResultProposed : RacePhase.ResultVerified))
                throw new IOException("Result acknowledgement mismatch.");
            if (request is SubmitResultRequest proposal && (ack.Result.Kind != proposal.Kind || !ack.Result.FinisherIds.SequenceEqual(proposal.FinisherIds)))
                throw new IOException("Finishers acknowledgement mismatch.");
        }
        if (status is null || status.RaceDayId != command.RaceDayId || status.RaceId != command.RaceId ||
            status.Version != checked(command.ExpectedRaceVersion + 1)) throw new IOException("Race acknowledgement mismatch.");
    }
}
