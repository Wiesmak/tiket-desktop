# Roadmap

Adapted from the inception roadmap for the existing .NET 11 / Rider solution. Each phase needs a separate review and explicit scope; this work does not start Phase 1.

| Phase | Deliverable / exit gate |
| --- | --- |
| 0 | Preserved native shell, mock cashier/admin, pinned contracts/clients, Windows builds/tests, documented UI evidence and remaining acceptance gaps |
| 1 | Harden responsive shell, focus/keyboard/Narrator/high-contrast validation, representative mock states and UI automation |
| 2 | Managed HTTPS/OIDC browser PKCE with compatible access tokens, role/resource handling, Ping and authorized known-event reads |
| 3 | Full read-only cashier composer and authoritative server quotes, stale quote invalidation, integer inputs |
| 4 | Durable encrypted exact purchase intent, unknown-outcome recovery, local recent tickets and backend print status/reprint; controlled cashier pilot only after failure tests |
| 5 | Complete local day wizard, immutable one-shot create, publish/start/race lifecycle and scoped enrollment/funding |
| 6 | Scratches/voids, result proposals/verification/settlement under distinct backend permissions |
| 7 | Accounts, movements, withdrawal quote/cashout, event completion/closure; complete all 37-method UI coverage |
| 8 | Operational resilience, privacy, measured cashier UX, multi-DPI/theme/accessibility and outage/crash validation |
| 9 | Approved signed MSIX distribution, update/rollback/journal migration, operator docs and controlled release |

Backend remains the only race/money/print authority. No silent financial retry, client settlement engine, offline bet or invented endpoint. A green compile does not satisfy local UI/accessibility or production readiness gates. CI has no release credentials.
