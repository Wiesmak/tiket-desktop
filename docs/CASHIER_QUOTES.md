# Cashier quotation, purchase and recovery

## Current Phase 5 operations

Sign in under Settings and select the provisioned event using the shared selector. Event/race state loads automatically. Choose the race, pool, horses and integer units; Get quote remains explicit. Select the authorized player/POS and review before Purchase. The selected balance and current print state refresh automatically. An unchanged poll preserves the quote and selections; changed race terms require a new explicit quote.

Receipts show compact purchase/reprint states; open Details for identifiers and historical acknowledgements. Reprint requires current-status inspection and one duplicate-output confirmation. An uncertain intent must be recovered with its original operator, connection and key; refresh never replays it. Retry appears for read failure, not as the normal cashier rhythm.

Administration uses a complete local plan before one-shot creation. Save/Resume is encrypted and actor/event-bound. After creation, Inspect saved plan is read-only. Publish, enroll/fund before start, Start and race Open/Close have explicit reviews and backend version checks. AUDITOR-only player listing does not grant enrollment/funding scope.

See VALIDATION_PHASE_5.md for actual executed tests. Historical notes below remain useful for protocol details; the current automatic-refresh and concise presentation above supersede older manual-refresh/mock descriptions.

---

In Settings, select a managed profile, sign in with the system browser, then read an authorized known event. Cashier lists only races and pools returned by that read. A day must be RUNNING and the race BETTING_OPEN. Refresh explicitly in Settings; there are no push updates or automatic re-quotes. Sign-in is not a quotation permission grant: the backend checks CASHIER/PLAYER and resource scope.

Choose WIN (first finisher), QUINELLA (first two in either order), EXACTA (ordered first/second), or TRIFECTA (ordered first/second/third). Only configured pools are offered.

- Normal: choose one explicit horse for each position. This maps to protobuf positional selection, not a separate variant.
- Wall: choose WALL in some positions and an explicit horse in at least one other position. WIN has no Wall mode. Duplicate explicit horses and all-wall choices are invalid.
- Box: select distinct horses from the bounded, scrollable native list. WIN, QUINELLA and EXACTA require at least two; TRIFECTA requires three. A box never contains WALL.

Labels preserve program numbers such as 02 and show display names; requests carry stable horse UUIDs. Scratched runners remain visible and disabled. A selected horse that becomes scratched remains selected with an error until explicitly changed. Refresh invalidates quotation even if the displayed versions did not change.

Units per combination accept ASCII digits for a positive signed 64-bit integer, at most 9223372036854775807. Blanks, signs, separators, fractions, overflow and overly long values are invalid. No NumberBox/double parsing is used. Get quote (Alt+Q or native keyboard activation) is explicit; edits do not transmit requests. Cancel quote discards its result, including a late response.

The summary displays returned canonical selection, counts, units, integer stakes/currency and versions. Currency units are not assumed cents. A quote reserves nothing and remains in memory until a deliberate purchase prepares its exact durable intent. No price, expansion, confirmation or version is computed locally.

Every relevant edit, event/race refresh, profile/operator change, logout, expiry, cancellation or window close clears the usable quote and suppresses older results. Even a newly returned quote can become stale. Enter an authorized player UUID, select a POS offered by the verified token, and choose Review purchase. The review shows the trusted cashier identifier (not sub), canonical terms, connection binding and an authorized account read. Confirm is a deliberate actual debit; Cancel sends nothing. Durable encrypted PREPARED and SENDING commits precede gRPC transmission. Editing or changing identity invalidates an open review.

ACKED means the exact matching acknowledgement was saved. UNKNOWN means a debit may have committed; do not start a replacement sale. Recover sends the byte-identical original request/key/coupon as the same verified identity and connection context, and can complete an uncommitted purchase. Do not edit the journal or change the key. If rights or storage are unavailable, preserve the encrypted data and escalate; never delete UNKNOWN. See SAFE_PURCHASE_JOURNAL.md.

Local history includes only this device's journal records visible under the current subject and grants. Current print reads use known ACKED job IDs. Inspect the original status before requesting one deliberate reprint; the warning covers duplicate output, backend retry and the serial receipt's missing visible REPRINT label. Reprint is a separately journaled mutation with its own key and no wager/debit. Never automatically reprint on failure or unknown physical output. No direct printer, enrollment, funding, settlement or withdrawal action is enabled by Phase 4.
