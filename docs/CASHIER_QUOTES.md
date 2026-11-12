# Cashier quotation (read-only)

In Settings, select a managed profile, sign in with the system browser, then read an authorized known event. Cashier lists only races and pools returned by that read. A day must be RUNNING and the race BETTING_OPEN. Refresh explicitly in Settings; there are no push updates or automatic re-quotes. Sign-in is not a quotation permission grant: the backend checks CASHIER/PLAYER and resource scope.

Choose WIN (first finisher), QUINELLA (first two in either order), EXACTA (ordered first/second), or TRIFECTA (ordered first/second/third). Only configured pools are offered.

- Normal: choose one explicit horse for each position. This maps to protobuf positional selection, not a separate variant.
- Wall: choose WALL in some positions and an explicit horse in at least one other position. WIN has no Wall mode. Duplicate explicit horses and all-wall choices are invalid.
- Box: select distinct horses from the bounded, scrollable native list. WIN, QUINELLA and EXACTA require at least two; TRIFECTA requires three. A box never contains WALL.

Labels preserve program numbers such as 02 and show display names; requests carry stable horse UUIDs. Scratched runners remain visible and disabled. A selected horse that becomes scratched remains selected with an error until explicitly changed. Refresh invalidates quotation even if the displayed versions did not change.

Units per combination accept ASCII digits for a positive signed 64-bit integer, at most 9223372036854775807. Blanks, signs, separators, fractions, overflow and overly long values are invalid. No NumberBox/double parsing is used. Get quote (Alt+Q or native keyboard activation) is explicit; edits do not transmit requests. Cancel quote discards its result, including a late response.

The summary displays the returned canonical selection, combination count, units/combination, total units, unit stake, total stake, currency and day/race versions. Monetary values are integer currency units, not assumed cents. The timestamp is local receipt time. Confirmation and the complete original response remain only in memory; no price, expansion, confirmation or version is computed locally.

Every relevant edit, event/race refresh, profile/operator change, logout, expiry, cancellation or window close clears the usable quote and suppresses older results. Quotations are non-binding: no wager is purchased and no funds are reserved. Even a newly returned quote can become stale. Review purchase remains disabled. No player ID or player balance is requested.

Historic ticket and latest-print cards are MOCK, with no real job or ticket. No purchase, reprint, funding, settlement, withdrawal, printer access or durable intent journal is implemented. Real packaged OIDC acceptance and real Tiket quotation remain separate unverified integration gates; synthetic tests establish neither.
