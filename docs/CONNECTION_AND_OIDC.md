# Connection and OIDC — Phase 2

## Provisioning and trust

An administrator provisions `%ProgramData%\Tiket Desktop\connections.json` using the version 1 format in [example](../config/connections.example.json) and [schema](../config/connections.schema.json). Field names are case sensitive. The runtime validates URI policy, IDs, scopes and duplicate profile IDs, and rejects unknown JSON fields. Restart to reload changes. No profile editor, import from a URL, or automatic profile selection exists.

Use Windows ACLs so only administrators/managed provisioning can change this directory and file. The app does not create or change machine ACLs. A user-editable file is not evidence of trust. Operators see and explicitly select the backend, issuer, public client and API audience before signing in. Replacing profiles requires restart; changing selection clears the session before any new endpoint is used. There is no saved active profile preference. Appearance remains session-only; protected storage is unnecessary for these non-secret values.

Backend URLs must be HTTPS origins without path, query, fragment or userinfo. Issuers must be exact HTTPS identifiers without query, fragment or userinfo. Discovery issuer equality is ordinal against the configured original string (including any trailing slash). Keep the issuer spelling consistent with provider metadata and backend JWT_ISSUER. Discovery endpoint validation remains enabled; cross-authority provider endpoints are not allowlisted. Windows certificate validation is never overridden. No remote plaintext, certificate ignore flag, redirected bearer request or implicit environment mode exists.

For explicit local development only: set DevelopmentLoopback=true and Backend to `http://127.0.0.1:9090` (port >1023). This permits h2c only to that literal loopback address. JWT security remains mandatory on the backend; the issuer still requires HTTPS. KnownEvents are nonempty UUID selections, never security grants.

## Register the identity provider

Register a **native/public** client without a client secret. Enable Authorization Code and PKCE S256. Request `openid` and the provider's configured API scope (example `tiket.read`); optionally `profile`. Register the loopback redirect path `http://127.0.0.1:{ephemeral-port}/oidc/callback`, using the provider's documented native-loopback arbitrary-port registration mechanism. Only the port varies. The TCP socket is opened exclusively on 127.0.0.1 before authorization, requires no HTTP.sys URL ACL/elevation, has a three-minute timeout and closes on completion/cancellation. Callback path, Host and state must match; one matching callback is accepted. Duplicate parameters and replay are rejected.

Providers restricted to fixed redirect ports are unsupported in this phase. A separately registered custom URI scheme needs a future design/packaged-activation test; there is no insecure fallback. Browser login never uses WebView2, password collection, implicit flow, ROPC or client credentials.

Duende.IdentityModel.OidcClient **7.1.1** (Apache-2.0) constructs Code+PKCE, validates state, exchanges the code and checks access-token hash when provided. Version 7 requires an explicit signature adapter. Microsoft.IdentityModel.JsonWebTokens **8.23.0** (MIT) validates signed ID tokens using discovered JWKS, exact issuer, client ID audience and lifetime. The adapter checks the validated nonce, nonblank subject and authorized-party binding. A fresh random nonce is requested for each login. Logging uses Duende's null logger; raw exception/provider descriptions are not shown. No ID token becomes an API credential.

The API audience is **not** the ID token audience: ID token aud is the native client ID. ApiAudience documents the backend expectation; issuance is configured through the provider's API scopes/mappers, not a vendor-specific audience request parameter. The backend validates the access token. Configure **RS256 JWT access tokens**, HTTPS issuer/JWKS, matching JWT_AUDIENCE, exp/nbf and nonblank sub. Opaque tokens and other signing algorithms cannot authorize Tiket reads. A rejected token shows re-authentication/audience/RS256 guidance.

Configure trusted array claims `tiket_roles` and `tiket_events`: at least one recognized role and the exact known race-day UUID. EVENT_ADMIN is not a financial superuser. Future cashier operations also need explicit `tiket_players`, `tiket_player`, `tiket_pos` and `tiket_cashier` mappings as specified by backend security docs. Profile UUIDs grant nothing.

## Backend and local TLS

Use backend commit d840ab12a08b06304b5e4d26568ba33e98a65ddc. Follow its docs/getting-started/local-development.md for PostgreSQL/Flyway and fixtures; do not create events through this desktop. An administrator must supply existing authorized event IDs.

For direct TLS configure SPRING_PROFILES_ACTIVE=production, TIKET_TRANSPORT=direct-tls, GRPC_TLS_ENABLED=true and SPRING_GRPC_SERVER_SSL_BUNDLE=grpc. Configure Spring's PEM SSL bundle `spring.ssl.bundle.pem.grpc.keystore.certificate=file:/path/server.crt` and `spring.ssl.bundle.pem.grpc.keystore.private-key=file:/path/server.key`. Set JWT_ISSUER, JWT_JWKS_URI (both HTTPS) and JWT_AUDIENCE to the provider's values. Keep keys outside this repository. Start backend using its documented Gradle bootRun/container route. Backend default gRPC port is 9090; verify effective deployment settings. Use a certificate whose SAN matches the configured host and whose chain Windows trusts; provision an approved development CA through normal Windows administration. Do not disable validation. A gateway may terminate TLS/HTTP2 externally; desktop still targets its HTTPS origin.

For local h2c use the backend's documented dev profile and literal loopback binding; dev retains JWT enforcement and still needs an HTTPS issuer. Never activate backend test security in an operational environment.

## Operator procedure

1. Open Settings / Connection; explicitly select the provisioned profile and verify endpoint and issuer.
2. Test API. Anonymous Ping reports process/API status, API version and test time. It does not establish database readiness, identity or event permission.
3. Sign in with browser. Complete provider sign-in and return to the app. Cancel or sign out closes pending authentication and requests.
4. Select a known event UUID and Read / refresh event. Actual server phase/version, date/currency/time zone and race/runner identifiers appear with a freshness time. GetRaceDay, ListRaces and a GetRace detail for the first returned race are called once per explicit refresh. No background polling occurs.
5. Read-only SERVER context appears in Cashier/Administration only after authorized loading. Existing print/ticket cards remain MOCK; quote, purchase, creation and all mutations remain disabled.
6. Sign out / switch operator immediately clears identity, credentials, event data and pending calls. Browser SSO cookies may remain. To choose another account, use the provider's account chooser/logout in the system browser. Global provider logout is not implemented.

Tokens are memory-only and lost on restart. ID/refresh tokens are not retained. No offline_access or refresh grant is requested; expiry or UNAUTHENTICATED clears credentials and requires interactive sign-in. Managed string memory cannot promise forensic zeroization or exclusion from externally captured process dumps; restrict dump collection/access operationally. No app dump exporter or token logging is introduced.

## Error handling

| State | Meaning / response |
| --- | --- |
| Unconfigured | Provision or repair managed JSON |
| Discovering / BrowserPending / Validating | Asynchronous login progress; cancel is available |
| DiscoveryError / BrowserError / CallbackError / ValidationError | Separate safe guidance for discovery/issuer/S256, default browser, callback registration/timeout, or rejected identity |
| SignedIn | Identity validated, event grants not yet established |
| Expired / UNAUTHENTICATED | Clear local session; check API token audience/RS256 and sign in |
| Denied / PERMISSION_DENIED | Session retained; correct scoped provider grants, do not probe opaque resources |
| UnknownEvent / NOT_FOUND | Authorized event/race absent |
| Offline / UNAVAILABLE / DEADLINE_EXCEEDED | Network/backend failure, not invalid credentials |
| Cancelled | User cancellation; old responses suppressed |

Each gRPC call has a ten-second deadline and cancellation. Channels are reused until profile/operator change or disposal. Ping carries no authorization header. Secured reads carry exactly one Bearer access token to the selected origin. No automatic retries or refresh loops exist. Generation checks prevent late results crossing sessions.

## Verification and limitations

Run the README restore/build/test/contract/package commands. The tests use a signed synthetic issuer through an in-process HTTPS protocol handler plus a real TCP callback, and an ephemeral real HTTP/2 gRPC wire endpoint. They are not external-provider compatibility evidence. No production test TLS bypass exists.

For an owner-configured real provider/backend: provision a disposable native client, trusted HTTPS certificates and two known events, granting only one. Launch with the README WinApp dotnet run route; verify anonymous Ping, browser return, operator/expiry, authorized event data, denial for the other event, logout clearing both workspaces, then profile/operator switching during a pending read. Repeat at 584x746, 930x746, 1440x746 and 1920x1080 in Light/Dark. Record issuer software/version and sanitized outcomes, never tokens/private account screenshots. Real-provider and packaged authenticated-read evidence is currently **unverified**.

No event directory, POS-wide history, financial engine, mutations, persistent refresh credentials, printer integration, signing or release is included.
