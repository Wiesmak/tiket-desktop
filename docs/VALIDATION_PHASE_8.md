# Phase 8 executive demo — 2026-11-13

PR: https://github.com/Wiesmak/tiket-desktop/pull/9 (unmerged).
Implementation/tested source: `f053f23c332ae67867ce9090fc19e7b186090968`.
Final documentation head and exact Windows CI run are pinned in the PR description.
Final-head CI: https://github.com/Wiesmak/tiket-desktop/pull/9/checks.
Base: merged Phase 7 `89bccbbf55c8f1ac3fbb8ade852f1afee4eb0553`.

- Desktop lab-only authenticated provisioning; explicit self-operator event/player grants; normal OIDC reissue and signed event discovery.
- Saved six-race wizard template, 54 distinct horse UUIDs, rosters 6/6/6/12/12/12, four pools, PLN unit stake 1, take 1000 bp.
- Persistent profile/event/player aliases; reviewed independently journaled roster enrollment.
- Guided server-quoted half-balance rounds; immutable protected intents, explicit cancellation and original-command recovery.
- Existing result revisions, scratch/void, real printing, account/cashout and closure controls retained.
- Event Results & Payouts screen and native CSV export; server account/settlement reconciliation, partial-data reporting.

Single controlled UI-only event: `77736bd4-672d-4fb7-88b2-95354a11854c`.
Native create/publish/start, 6 races, 24 accounts, aliases Player 01–24; opening paid 0, promotional 4800 PLN.
Purchases by race: **24, 24, 24, 24, 24, 24 = 144**; zero bankrupt skips.
Stakes by race: **2400, 1800, 1500, 1356, 1272, 636 = 8964 PLN**.
Every race closed, official order 01 → 02 → 03 submitted/verified, then SETTLED; day COMPLETED v4.
All six race winners reports reconcile. Final report: 6 results, 24 pools, 72 winning-leg rows, all 24 accounts.
Final totals PLN: opening 4800 + credited winnings 4800 − stakes 8964 + refunds 0 = balance **636**.
No cashout/forfeiture performed. Native CSV `.awb/phase8-final.csv` exported from the displayed complete report.
Read-only journal check: 144 ACKs; 144 distinct command IDs/coupon IDs/keys; exact manifest identity matches; no event UNKNOWNs.
Desktop restart/resume retained the same event, aliases and acknowledged round; historical events/journals/volumes preserved.
All business effects originated in visible supported desktop controls via normal authenticated transport; automation only clicked/typed in UI.
No backend/protobuf changes, grant scripts, seed/reset, SQL writes, injected state or internal-session business calls.

One full local suite: **343 passed** (15 new targeted preparation/provisioning-guard/resume/reconciliation tests).
Debug solution build: zero warnings/errors; `git diff --check` passed.
Repairs during the same run: canonical QUINELLA comparison, signed-selector read synchronization, paced event report reads.
Lab service credentials remain solely in its environment; exact lab issuer/loopback profile required. Workflow: PHASE_8_LAB_WORKFLOW.md.
No signing/release/deployment deliverable. Phase 9 retains packaging/workstation smoke scope.
