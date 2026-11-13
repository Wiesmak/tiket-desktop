using Google.Protobuf;
using Grpc.Core;
using Pl.Umamusume.Tiket.Api.V1;

namespace Tiket.Desktop.Application;

/// <summary>Internal prerequisite; callers must still supply a validated live session and explicit review.</summary>
internal sealed class JournaledMutation(MutationJournal journal)
{
    // The durable state transition arbitrates competing calls; no retry loop is allowed here.
    internal async Task<MutationRecord> SendAsync(Guid intentId, MutationBinding authenticatedBinding,
        Func<MutationRecord, CancellationToken, Task<byte[]>> transmit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sending = journal.MarkSending(intentId, authenticatedBinding);
        try
        {
            // Dispatch receives a copy, so neither the caller nor the transport can edit the saved original.
            var bytes = await transmit(sending, cancellationToken).ConfigureAwait(false);
            VerifyAcknowledgement(sending, bytes);
            // Save even after sign-out/cancellation if the response was received. UI generation is a separate concern.
            return journal.Acknowledge(intentId, authenticatedBinding, bytes);
        }
        catch (Exception error)
        {
            // Even denial/precondition is conservatively uncertain: an earlier transmission may have committed.
            // If this write also fails, the durable SENDING marker recovers to UNKNOWN; journal stays quarantined.
            try { journal.MarkUnknown(intentId, authenticatedBinding); } catch { }
            throw new MutationUncertainException(error is RpcException rpc ? rpc.StatusCode : null);
        }
    }

    private static void VerifyAcknowledgement(MutationRecord original, byte[] bytes)
    {
        if (original.Method == "pl.umamusume.tiket.api.v1.BettingService/PurchaseCoupon")
        {
            var request = PurchaseCouponRequest.Parser.ParseFrom(original.Request);
            var response = PurchaseCouponResponse.Parser.ParseFrom(bytes);
            var quote = QuoteCouponResponse.Parser.ParseFrom(original.ReviewedQuote);
            if (response.CouponId != request.CouponId || response.PlayerId != request.PlayerId ||
                response.Pricing is null || !response.Pricing.Equals(quote.Pricing) ||
                !Guid.TryParse(response.PrintJobId, out _) || response.AccountVersion < 0 ||
                response.PrintingStatus == DispatchStatus.Unspecified)
                throw new InvalidOperationException("Acknowledgement did not match reviewed intent.");
        }
        else
        {
            var response = PrintStatus.Parser.ParseFrom(bytes);
            if (response.CouponId != original.Coupon || response.Type != PrintJobType.Reprint ||
                !Guid.TryParse(response.PrintJobId, out _) || response.Status == DispatchStatus.Unspecified)
                throw new InvalidOperationException("Reprint acknowledgement did not match intent.");
        }
    }
}

internal sealed class MutationUncertainException(StatusCode? code = null) : IOException(
    "Outcome uncertain. Preserve the original intent; explicit same-identity recovery may complete a purchase.")
{
    internal StatusCode? Code { get; } = code;
}

internal static class MutationMessages
{
    internal static string For(StatusCode code) => code switch
    {
        StatusCode.Unauthenticated => "Authentication rejected. Sign in as the original operator before recovery.",
        StatusCode.PermissionDenied => "Resource access denied. Preserve the intent and ask an administrator to restore grants.",
        StatusCode.InvalidArgument => "Request rejected as invalid. Preserve the original intent for investigation.",
        StatusCode.NotFound => "Authorized resource unavailable. Absence does not prove no prior purchase.",
        StatusCode.AlreadyExists => "Identity or idempotency conflict. Do not create a replacement key.",
        StatusCode.Aborted => "Version conflict. A changed sale requires a fresh quote and explicit review.",
        StatusCode.FailedPrecondition => "Purchase precondition failed. Inspect current server state before a new sale.",
        StatusCode.ResourceExhausted => "Server request or expansion limit reached. Preserve the original intent.",
        StatusCode.Unavailable => "Backend unavailable. Outcome uncertain after transmission.",
        StatusCode.DeadlineExceeded => "Deadline exceeded. Outcome uncertain after transmission.",
        StatusCode.Cancelled => "Request cancelled. Outcome uncertain after transmission.",
        _ => "Unexpected response. Preserve the original intent for investigation."
    };
}
