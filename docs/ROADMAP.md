# Roadmap

Adapted from the inception roadmap for the existing .NET 11 / Rider solution. Each phase needs a separate review and explicit scope. Phases 0–2 are merged; Phase 2 real-service integration remains provisional. Phase 3 is scoped to read-only quotation.

| Phase | Deliverable / exit gate |
| --- | --- |
| 0 | Preserved native shell, mock cashier/admin, pinned contracts/clients, Windows builds/tests, documented UI evidence and remaining acceptance gaps |
| 1 | Responsive shell, visible latest mock status, distinct local builder steps, keyboard/focus/theme checks and Windows CI; full accessibility certification deferred to Phase 8 |
| 2 | Managed HTTPS/OIDC browser PKCE with compatible access tokens, role/resource handling, Ping and authorized known-event reads |
| 3 | Full read-only cashier composer and authoritative server quotes, stale quote invalidation, integer inputs |
| 4 | Durable encrypted exact purchase intent, unknown-outcome recovery, local recent tickets and backend print status/reprint; controlled cashier pilot only after failure tests |
| 5 | Complete local day wizard, immutable one-shot create, publish/start/race lifecycle and scoped enrollment/funding |
| 6 | Scratches/voids, result proposals/verification/settlement under distinct backend permissions |
| 7 | Accounts, movements, withdrawal quote/cashout, event completion/closure; complete all 37-method UI coverage |
| 8 | Operational resilience, privacy, measured cashier UX, multi-DPI/theme/accessibility and outage/crash validation |
| 9 | Approved signed MSIX distribution, update/rollback/journal migration, operator docs and controlled release |

Backend remains the only race/money/print authority. No silent financial retry, client settlement engine, offline bet or invented endpoint. A green compile does not satisfy local UI/accessibility or production readiness gates. CI has no release credentials.

## Phase 2 delivery status — 2026-11-12

Implemented profile/session/browser and read-only generated gRPC infrastructure plus native Settings overview. Local unit/protocol/transport tests and build evidence are recorded in VALIDATION_PHASE_2.md. External-provider smoke is unavailable by owner confirmation; packaged authenticated callback and actual Tiket data remain unverified acceptance gates. Do not mark Phase 2 operationally complete or start Phase 3 on the basis of synthetic tests. Phase 3 requires owner review, a compatible provisioned HTTPS provider/backend and completion of outstanding native acceptance, then a separately scoped read-only quote design. No Phase 3–9 operation is enabled here.

## Phase 3 scope — 2026-11-12 (supersedes archival kickoff restriction)

Owner authorized pure Phase 3 implementation/testing after Phase 2 PR #3 merge while carrying real-service gates forward. PR #3 merged at 98ffa6d4e58883129a7b352c9fb1c19077b9a594 with successful merge CI 43245964408. Phase 3 implements Normal/Wall/Box, all four pools, exact int64 units and cancellable server quotation. See VALIDATION_PHASE_3.md for review status and evidence. Real OIDC packaged smoke and real Tiket quote remain separately unverified. Phase 4 has not started; purchases and all financial/admin mutations remain disabled. Trusted real identity/backend acceptance and durable exact-intent recovery remain mandatory before Phase 4 operational acceptance.
