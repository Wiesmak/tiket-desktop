# Identity and security boundaries

Adapted from the inception security design. Phase 0 implements no authentication, credential storage, backend calls or local financial journal.

Future trusted managed profiles specify HTTPS backend, exact issuer, public native client ID, API audience/scopes, redirect policy and known event/POS references. Do not accept arbitrary issuer changes during login, embed a client secret, disable certificate validation or send an ID token as a backend bearer token. Browser Authorization Code + PKCE requires state/nonce verification and backend-compatible access tokens. The backend currently expects RS256, configured issuer/JWKS/audience, exp/nbf/sub and trusted claim mappings.

Roles are CASHIER, EVENT_ADMIN, RESULT_SUBMITTER, RESULT_VERIFIER, SETTLEMENT_OPERATOR, AUDITOR and PLAYER. Explicit event/player/POS grants and exact cashier identity remain server enforced. EVENT_ADMIN does not imply financial access; ListPlayers requires AUDITOR. Navigation in this mock is not a permission implementation.

Future tokens should be memory-only where possible; refresh storage is per Windows user and issuer/client/subject. DPAPI CurrentUser alone does not separate cashiers sharing a Windows account. Operator switching must clear credentials/caches while preserving segregated UNKNOWN intents for same-subject recovery. Never log tokens, auth codes, raw financial requests, player histories or secrets.

Unknown mutations require durable exact-request/idempotency replay, not new coupon IDs. Backend printing remains authoritative; DISPATCHED means transfer, not confirmed paper. CashOut records accounting, not physical disbursement. No printer/device/admin capability is needed. The existing manifest keeps runFullTrust only; systemAIModels was removed before this branch. Publisher identity is preserved. No signing certificates, private keys or release secrets are added.

Backend [permission and identity contract](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/authentication.md) and [error/replay rules](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/errors-and-idempotency.md) take precedence. Native login design follows [RFC 8252](https://www.rfc-editor.org/rfc/rfc8252).
