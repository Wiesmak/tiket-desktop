# Phase 5 validation — work in progress

2026-11-13, Europe/Warsaw. **Phase 5 is incomplete and not operationally accepted.** This record distinguishes implemented foundations from remaining delivery work.

## Baseline and preservation

Fetched origin/master before editing. Clean working tree and exact base ea4832c22ae4c313e5ee9e894cf51a296c110633 (merged Phase 4 PR #5). Rechecked hosted post-merge CI 43382833996: completed/success at that exact SHA. Branch feat/phase5-admin-ops-autorefresh. Existing .awb evidence, native purchase journal and lab records were preserved. No backend source, contract, identity provider configuration, managed profile, production service or database record was changed.

The three existing Compose services were running; PostgreSQL healthy. Backend ResourceAuthorization and RpcPolicy were read as source references. EVENT_ADMIN, CASHIER and AUDITOR paths remain distinct; no invented result or settlement role.

## Implemented foundations

- Dispatcher-driven RefreshCoordinator: one in-flight read, two-second minimum request separation, ten-second active cashier / thirty-second other-context cadence, bounded deterministic error backoff, coalesced requests, cancellation on context/foreground/shutdown changes. No mutation callback, persisted bearer or automatic replay.
- Separate session synchronization retains identical protobuf snapshots and existing editor controls/quotes; a material day/race change invalidates quoted terms while preserving explicit selections. Denied/not-found polling stops until explicit Retry or a context change. Offline state retains last-known data.
- Sign-in and event selection initiate automatic reads. Visible print status reads share the coordinator, are limited to ten local rows, and stop after terminal state. Context cancellation suppresses old display updates. Historical acknowledgements remain separate from current print state.
- Shorter cashier heading/purchase confirmation, removed persistent warning banner and redundant history refresh; normal purchase/reprint grants and journal gates remain enabled.
- LocalRaceDayPlan model validates provisioned event scope, dates/time zones, IDs/numbers/labels, all four pools and exact int64 stake constraints. Frozen plans retain one exact request/key across serialization and cannot be edited. **This model is not yet wired to a functioning native wizard or save/resume controls.**
- Separate AdministrationJournal schema/file, DPAPI CurrentUser encryption, exclusive writer, durable replacement, SENDING-to-UNKNOWN reopen, immutable copies and original-context exact replay foundation. PurchaseJournal v1 was not migrated or repurposed.
- Seven allow-listed generated admin/funding mutation transports and enrollment/player read transports; independent signed administration grant validation. **These internal foundations are not yet connected to reviewed native admin operations.**

## Executed checks

225 deterministic tests pass: all 200 baseline cases plus 25 new cases. New cases cover virtual read cadence/coalescing/backoff/pause, non-overlap and actor-context cancellation, unchanged quote retention and changed version invalidation; varied 1/3/12/32 runner plans, leading-zero labels, duplicate IDs/numbers, leap-date/time-zone and pool validation, frozen request round trip; separate encrypted admin journal, multiwriter exclusion, durable send failure, unknown reopen and exact original-key recovery with a synthetic response.

Local Debug solution build passed with zero warnings/errors. Release native build and unsigned MSIX passed; existing missing mspdbcmf.exe symbols-package warning remains. All four contract checksums match. No dependencies or version pins changed.

Real registered development-package browser Code+PKCE login as lab-cashier succeeded autonomously. Authorized original-event data loaded automatically without invoking Read/Retry. Real WIN quote succeeded. A 25-second UIA probe retained exact quote text, focused units editor and enabled purchase review across unchanged reads. No new purchase/reprint/admin mutation was invoked.

The registered AppX layout initially contained older binaries despite a normal build. Old-label observations were rejected as current-source evidence. The development layout was refreshed from current Debug output before the current native smoke. This was local development staging, not installation of a signed distribution.

Two early focus probes were invalid: UIA had focus in the IDE rather than the native window. Activating the native process first corrected the harness; the subsequent probe passed. Those failures are not hidden or represented as passing tests.

Ignored local evidence/scripts: .awb/phase5-native-current, .awb/phase5-native-drive.ps1, .awb/phase5-retention.ps1. No fictional business identifiers were redacted. Password values were loaded only by the existing trusted-browser automation and never printed.

## Required work still outstanding

- Complete native multirace wizard, local encrypted save/resume, exact-payload review, unused-slot detection and one-shot create.
- Session-integrated durable admin review/recovery with independent role/event/player checks, lifecycle controls and enrollment/funding UI.
- Third-event/new-player lab provisioning and fresh admin/cashier/outsider browser tokens.
- Real CreateRaceDay, publish, enrollment/funding, start/open/close tests and denials; real commit/suppressed-ack/fresh-process exact-key admin recovery.
- Current account/balance synchronization and comprehensive refreshed-state/version modal handling.
- Full admin transport/security/journal fault matrix and native admin keyboard/resume tests.
- New-event purchase/reprint/recovery regression. Existing Phase 4 lab records were deliberately not mutated.
- Complete four-size Light/Dark native review, hosted final-head CI and final acceptance evidence.

No environment blocker for these remaining tasks has been established. Synthetic journal tests are not real admin integration proof. Do not approve this branch as a completed Phase 5 deliverable.
