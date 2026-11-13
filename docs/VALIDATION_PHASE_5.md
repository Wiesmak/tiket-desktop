# Phase 5 validation — 2026-11-13

Phase 5 functional and local-lab acceptance is complete. PR #6 remains unmerged; readiness requires successful Windows CI at its final head. Exact final SHA/run are recorded in the PR checks and ignored successor handoff. No Phase 6 implementation, signing, tag, distribution or production change.

## Source and environment

Continued the existing feat/phase5-admin-ops-autorefresh branch from reviewed b35a71434b641c756d83077d547098c3be82aa69, without a new branch or PR. Base remains ea4832c22ae4c313e5ee9e894cf51a296c110633, merged Phase 4. Baseline post-merge CI 43382833996 and foundation CI 43388281510 passed. Functional implementation commit b81c972b0c0f3d392ff5354c20778f7440c295ff passed hosted Windows CI [43405606690](https://github.com/Wiesmak/tiket-desktop/actions/runs/43405606690); final source/evidence successors are checked again before Ready.

The read-only backend checkout remains clean at 1759f3afef21b9523db918ab7712666709d3453b. Snapshot/upstream authority remains d840ab12a08b06304b5e4d26568ba33e98a65ddc. Running-image-to-source provenance remains unverified, as in Phase 4; do not substitute a guessed runtime commit. Four pinned contract checksums and package/project identities are unchanged.

Existing Compose PostgreSQL 18, Keycloak and console-outbox backend were preserved. No seed rerun, realm reset, data-volume deletion, SQL write or mutation of seeded event …0001/negative selector …0002 occurred. A short backend-container pause/unpause tested outage behavior; finally restored it.

## Actual disposable fixture

| Resource | Actual value |
| --- | --- |
| Profile / backend | disposable-tiket-lab / http://127.0.0.1:9090 |
| Issuer / public client / audience | https://localhost:8443/realms/tiket-lab / tiket-desktop / tiket-api |
| New third event | a9c4f03e-98cf-4587-a8c6-9759c742292a |
| New player | e53ffc71-667e-4b0d-8c83-df50070aa4ec |
| First race | 916523d6-e19b-4bc1-b2f6-c86dda75d7c8 |
| Second race | d0dbe83b-74ce-4611-853d-fd72cb19f209 |
| Roster / pools | Three actual generated horse UUIDs per race; ordered labels 01, 002, 03; WIN/QUINELLA/EXACTA/TRIFECTA stakes 10/20/30/40, take 1000 basis points |
| Day basics | 2026-11-13, display 13, PLN, Europe/Warsaw, lab-phase5-v1, 10000 legs, one concurrently open race |

Existing admin/cashier event/player attributes were backed up and extended, preserving old grants; outsider remained out of scope. ProgramData KnownEvents was backed up and extended, retaining both original selectors. Automatic approval review initially rejected changing unmanaged attributes; the owner explicitly approved lab-only ADMIN_EDIT, which was then applied with a profile backup. Roles, sub mapper, profile scope, signing keys, TLS trust, PKCE and audiences were not weakened. Fictional required email/name metadata was completed through the normal browser. Fresh signed API grants were verified by the production login/session and authorized reads. Credentials/tokens were never printed or committed.

Configuration backups/fixture manifest are ignored under .awb: phase5-user-profile-before.json, phase5-lab-admin-attributes-before.json, phase5-lab-cashier-attributes-before.json, phase5-connections-before.json and phase5-lab-manifest.json. Keep the new grants/profile selector while these durable records need recovery. Restoration instructions are in the local handoff; do not reset the database incarnation or remove scope under unresolved commands.

## Executed gates

| Gate | Result | Direct evidence |
| --- | --- | --- |
| Baseline regression and deterministic additions | PASS | 283 tests, zero failures/skips: all 225 previous cases plus 58 completion cases. Temporary DPAPI journals, signed synthetic OIDC and real synthetic HTTP/2; no lab dependency in CI. |
| Complete native local wizard | PASS | Two races entered through native controls, ordered UUID rosters/leading-zero labels and all four pool rules. Model validation includes varied 1/3/12/32 rosters, duplicates, leap date/time zone, int64 bounds, day number 1–999 and backend-supported take 0–10000. |
| Encrypted Save/Resume and immutability | PASS | Native encrypted save, app close/restart, fresh admin PKCE, selected third event and Resume restored the first race. Read-only SQL count was zero before Create. Frozen saved definition later compared equal to complete GetRaceDay definition. Read-only inspection across race switches, runner and pool fields passed; created definition cannot be resubmitted. |
| Native create/read-back/publish | PASS | Create returned DRAFT v0; actual GetRaceDay/ListRaces returned two real races and exact immutable definition. Native Publish returned READY v1. No replacement create key was generated after dispatch. |
| Native enrollment/funding/reads | PASS | Enroll 1000 paid +100 promotional returned balance 1100/account v0. Additional 50+10 before start returned balance 1160/v1. GetEnrollment/GetAccount updated automatically; auditor ListPlayers contained the new player. One account ledger, no separate wallet inference. |
| Native start/open/close lifecycle | PASS | Start RUNNING/day v2; first open race v1, first close v2; second open v1, second close v2. Maximum one open race respected. Final day remains RUNNING v2, both races BETTING_CLOSED v2; no settlement. |
| Native new-event purchase and print | PASS | Cashier PKCE, real WIN quote, explicit reviewed journal-first 10 PLN purchase. Current balance 1150/account v2 and ORIGINAL Dispatched appeared without manual Refresh. Purchase/reprint controls were enabled with legitimate grants. |
| Reprint and exact financial replay regression | PASS | One explicit native reprint, distinct REPRINT job. Fresh-process same-key purchase/reprint replays through encrypted test journals returned byte-identical original acknowledgements. Native Phase 4 snapshot reopened and retained the historical ed3ce775… purchase. No extra coupon, debit or job. |
| Real admin commit/lost ACK/fresh native recovery | PASS | Test-only wrapper committed second-race CloseBetting, encrypted/flushed captured ACK, then suppressed delivery. Native admin journal retained UNKNOWN; process exited. Wrong subject disclosed/replayed nothing, zero transmissions. Fresh packaged admin login displayed actual UNKNOWN and explicitly recovered through shipped UI. Separate audit verified unchanged request hash, identical captured/saved ACK and race version 2 before/after replay. |
| Role/scope/stale/key denials | PASS | Real cashier Publish denied; wrong-player enrollment denied under cashier and combined admin/auditor; outsider third-event read denied through fresh browser PKCE and native UI. Stale version ABORTED, duplicate day ALREADY_EXISTS, same original create key/different payload ALREADY_EXISTS. No automatic retry. EVENT_ADMIN-only signed-session tests do not require CASHIER/POS grants. |
| Refresh, offline and isolation | PASS | Virtual-time frequency/coalescing/backoff/pause/non-overlap/late-response cases. Identical snapshots retain quote; material changes invalidate review while preserving selections. Native 25-second quote/focus/enabled-review retention passed. Actual backend pause produced Offline plus stale timestamp; automatic reconnect retained quote/review. Account versions and terminal print state use server reads; actor/event switches cancel and suppress old display callbacks. |
| Native layout, keyboard, themes | PASS | Eight actual cashier captures plus all five saved-plan steps × four sizes × Light/Dark (40 captures), 96 DPI. Actual native UNKNOWN, denied and offline captures are separate. Created-plan read-only UIA probe includes leading-zero label and race switching. Existing reduced-motion and native shell behavior retained. |
| Builds, contracts, diagnostics and security | PASS | Full local x64 Debug/Release; unsigned MSIX; four hashes; whitespace; no dependency/pin/schema/backend changes. Rider error inspections clean after making the admin coordinator constructor explicit. No token/credential or plaintext-journal export in source/evidence. |
| Hosted Windows CI | PASS / final-head gate | Implementation CI 43405606690 passed; final successor checks must also pass before Ready. Exact final run/head are recorded in PR #6 and the ignored handoff, avoiding a self-referential commit SHA in this file. |

The backend exposes an opaque quote confirmation rather than a separate quote UUID. The original confirmation remains in the encrypted purchase intent; it was not exported as evidence.

## Durable effects and exact recovery

| Effect | Actual identifier / invariant |
| --- | --- |
| Purchased coupon | 41449acf-8f38-4220-a458-024c99b010ca; one 10 PLN COUPON_STAKE movement |
| ORIGINAL job | cbc4f41c-29e3-4e50-9fb8-cd209877ab1a; DISPATCHED |
| REPRINT job | 64541992-40b7-45e0-b07f-2370302e428f; DISPATCHED; no financial movement |
| Lost-ACK admin intent | ce0e1ca9-df06-428c-a95a-1bf825d4f17e; CloseBetting for second race |
| Original/recovered request SHA-256 | EE2F3F9774386B678F3FE8C2AF69B503C84E452EFE7631FA2A7A40A3B0EE9405 |
| Recovery invariant | UNKNOWN → fresh native explicit replay → ACKED; captured and saved ACK bytes equal; committed/recovered race version both 2 |

Final event-scoped BEGIN READ ONLY verification: one coupon totaling 10, one COUPON_STAKE movement totaling 10, one ORIGINAL and one REPRINT, one opening-funding adjustment, balance 1150/version 2/accepted stakes 10. command_actor contains one Create, READY, Enroll, Funding, RUNNING, Purchase and Reprint reservation, and exactly one Open/Close per race. Recovery did not add a reservation or increment a version.

There were 20 deliberate Tiket mutation RPC transmissions: nine intended admin effects plus one original-key admin recovery and six expected rejected admin/onboarding requests; one intended purchase plus its saved-key replay; one intended reprint plus its saved-key replay. Wrong-subject recovery sent zero. Read/quote/auth/configuration probes are not counted as mutations.

The lost-ACK wrapper lives only in ignored .awb/phase5-live-admin. It used the production session/coordinator/generated transport against the actual backend and existing native AdministrationJournal while the native app was closed; all existing records were retained. This is a real process boundary and fresh native-app recovery, not synthetic success or a claim of hardware power-loss certification. The captured ACK is DPAPI encrypted. No shipping fault-injection flag exists.

## Evidence and resolved test-harness issues

Ignored evidence: .awb/phase5-completed-native (48 actual captures), phase5-native-real-unknown, phase5-completed-native-denied, phase5-completed-offline, phase5-completion-plan-resumed, phase5-completion-purchased, phase5-live-logs, phase5-admin-drop-evidence.json, phase5-admin-recovery-evidence.json and encrypted capture/replay journals. No fictional business IDs, stakes or balances were redacted.

Early failed probes selected stale browser-tab controls, required missing fictional user-profile fields, compared IDE rather than app focus, or searched offscreen/hidden controls. They were corrected and rerun; they were not treated as passing acceptance or followed by duplicate financial confirmations. One read-only SQL probe used an incorrect movement column name and was corrected to movement_type. Existing evidence was retained.

## Carry-forward limits

No Phase 5 functional acceptance gap is deferred to Phase 6. Carry-forward deployment limits remain: DPAPI separates Windows accounts, not OIDC operators sharing one Windows login; storage-hardware power-cut certification, archival/migration/backup tooling, broader Narrator/high-contrast/multi-DPI certification, physical paper delivery, signed clean-machine distribution and exact runtime image/source attestation remain later deployment work. Console DISPATCHED is adapter acceptance. Working authorized development controls remain enabled.

Phase 6 is not started. ROADMAP.md retains the binding AUDITOR-only post-SETTLED winning-coupon/player report requirement and endpoint/settlement-ID limitations.
