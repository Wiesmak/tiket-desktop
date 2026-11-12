# Identity and security boundaries

Adapted from the inception security design. Phase 0 implements no authentication, credential storage, backend calls or local financial journal.

Future trusted managed profiles specify HTTPS backend, exact issuer, public native client ID, API audience/scopes, redirect policy and known event/POS references. Do not accept arbitrary issuer changes during login, embed a client secret, disable certificate validation or send an ID token as a backend bearer token. Browser Authorization Code + PKCE requires state/nonce verification and backend-compatible access tokens. The backend currently expects RS256, configured issuer/JWKS/audience, exp/nbf/sub and trusted claim mappings.

Roles are CASHIER, EVENT_ADMIN, RESULT_SUBMITTER, RESULT_VERIFIER, SETTLEMENT_OPERATOR, AUDITOR and PLAYER. Explicit event/player/POS grants and exact cashier identity remain server enforced. EVENT_ADMIN does not imply financial access; ListPlayers requires AUDITOR. Navigation in this mock is not a permission implementation.

Future tokens should be memory-only where possible; refresh storage is per Windows user and issuer/client/subject. DPAPI CurrentUser alone does not separate cashiers sharing a Windows account. Operator switching must clear credentials/caches while preserving segregated UNKNOWN intents for same-subject recovery. Never log tokens, auth codes, raw financial requests, player histories or secrets.

Unknown mutations require durable exact-request/idempotency replay, not new coupon IDs. Backend printing remains authoritative; DISPATCHED means transfer, not confirmed paper. CashOut records accounting, not physical disbursement. No printer/device/admin capability is needed. The existing manifest keeps runFullTrust only; systemAIModels was removed before this branch. Publisher identity is preserved. No signing certificates, private keys or release secrets are added.

Backend [permission and identity contract](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/authentication.md) and [error/replay rules](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/errors-and-idempotency.md) take precedence. Native login design follows [RFC 8252](https://www.rfc-editor.org/rfc/rfc8252).

## Phase 2 implementation (supersedes future login/token wording above)

Tokens are memory-only; access tokens expire into interactive re-authentication. ID/refresh tokens are not retained, offline_access is rejected, and no credential vault is used. Profiles are machine-managed non-secret JSON under ProgramData with administrator-controlled ACLs. Explicit selection shows backend/issuer/client/audience and invalidates prior state. Loopback TCP is bound to 127.0.0.1 on an OS-assigned port, with one matching path/Host/state callback, timeout, cancellation and disposal. Duende 7.1.1 plus Microsoft IdentityModel 8.23.0 validate Code+S256, signed ID identity, issuer/client audience/lifetime and nonce binding. No certificate bypass, remote h2c or redirected bearer request exists. Local logout does not clear system-browser SSO. CLR strings are not a promise of forensic zeroization; no crash-dump exporter is implemented.

Backend grants remain authoritative. PERMISSION_DENIED retains identity and clears failed read data; UNAUTHENTICATED clears session. Offline/deadline failures do not invalidate credentials. Test fixture authentication is not evidence for an external provider. See [connection guide](CONNECTION_AND_OIDC.md) and [validation](VALIDATION_PHASE_2.md).

## Phase 3 read-only quotation

QuoteCoupon is the only newly invoked RPC. It uses the managed backend channel, exactly one access-token Bearer header, ten-second deadline and linked session/editor cancellation. Backend CASHIER or PLAYER authorization and resource scope remain mandatory; navigation and overview reads confer no quote grant. No player identifier or balance is needed.

Editor revision plus session generation and cloned event/race context prevent old results crossing selections, refreshed rosters/statuses, operators or profiles. Units parse directly as positive int64 ASCII text. Pricing/canonical terms, versions and opaque confirmation remain exactly from the server in memory only, without reservation or durable financial intent. Errors are sanitized; no raw server message, token or sensitive payload is logged or persisted.

Purchase/reprint/admin/funding/settlement/withdrawal calls, token persistence, offline betting and printer access remain absent. Phase 4 requires real packaged OIDC/transport acceptance and a separately reviewed durable encrypted exact-request/idempotency recovery design. Synthetic OIDC, HTTP/2 fixtures and injected visual evidence do not close real-service gates.
