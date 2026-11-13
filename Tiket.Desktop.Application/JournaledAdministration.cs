using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

/// <summary>Internal prerequisite; callers must still supply a validated live session and explicit review.</summary>
internal sealed class JournaledAdministration(AdministrationJournal journal)
{
    // The durable state transition arbitrates competing calls; no retry loop is allowed here.
    internal async Task<AdministrationRecord> SendAsync(Guid intentId, AdministrationBinding authenticatedBinding,
        Func<AdministrationRecord, CancellationToken, Task<byte[]>> transmit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var firstTransmission = journal.Read(authenticatedBinding).Single(r => r.Id == intentId).State == MutationState.Prepared;
        var sending = journal.MarkSending(intentId, authenticatedBinding);
        try
        {
            // Dispatch receives a copy, so neither the caller nor the transport can edit the saved original.
            var bytes = await transmit(sending, cancellationToken).ConfigureAwait(false);
            AdministrationWire.Verify(sending, bytes);
            // Save even after sign-out/cancellation if the response was received. UI generation is a separate concern.
            return journal.Acknowledge(intentId, authenticatedBinding, bytes);
        }
        catch (Exception error)
        {
            // Even denial/precondition is conservatively uncertain: an earlier transmission may have committed.
            // If this write also fails, the durable SENDING marker recovers to UNKNOWN; journal stays quarantined.
            try
            {
                // An explicit server rejection of the first transmission is terminal. A replay rejection
                // cannot disprove a previous commit, so UNKNOWN recovery stays conservative.
                if (sending.State == MutationState.Sending && error is RpcException rejected &&
                    journal.Read(authenticatedBinding).Single(r => r.Id == intentId).Response is null &&
                    firstTransmission && rejected.StatusCode is StatusCode.Aborted or StatusCode.InvalidArgument or
                        StatusCode.FailedPrecondition or StatusCode.PermissionDenied or StatusCode.Unauthenticated or StatusCode.NotFound or StatusCode.AlreadyExists)
                    journal.MarkRejected(intentId, authenticatedBinding);
                else journal.MarkUnknown(intentId, authenticatedBinding);
            }
            catch { }
            throw new MutationUncertainException(error is RpcException rpc ? rpc.StatusCode : null);
        }
    }

}
