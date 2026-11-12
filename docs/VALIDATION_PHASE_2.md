# Phase 2 evidence — 2026-11-12

## Baseline and scope

Fetched origin before branching. Current master/base: **8af3c3c17aabe64b919c19f4d55809135aa7cc10**, Phase 1 PR #2 merged; prior final head 7c6ff97b000f3194f965ec521387afc429318113 and successful merge CI 43233732100 remain historical. Created feat/phase2-oidc-grpc from origin/master. Only untracked .awb/ existed; preserved. Backend ls-remote still returned d840ab12a08b06304b5e4d26568ba33e98a65ddc. No refresh/change to the four protobuf files, backend, package identity, platform, assets or license.

Read the supplied merged handoff, all required repository docs and four contracts. Rider read_file rejects the Downloads handoff outside project roots, so it was read through Rider's integrated terminal. Backend documentation and ResourceAuthorization/RpcPolicy/SecurityConfiguration were read from an isolated read-only checkout under .awb/backend-readonly. OIDC source was inspected at upstream tag imoc-7.1.1, commit ab00474eb5311c3b4431b992e7296e779f5904aa, under .awb/oidc-reference; neither checkout is committed.

Host: Rider 2026.3, Windows 11 x64, .NET SDK 11.0.100. Existing target net11.0-windows10.0.26100.0 / OS floor 22000 / WindowsAppSDK 2.6.5 unchanged. New direct packages: Duende.IdentityModel.OidcClient 7.1.1 (Apache-2.0), Microsoft.IdentityModel.JsonWebTokens 8.23.0 (MIT). Duende requires an explicit signature validator; Microsoft IdentityModel supplies JWT signature/issuer/audience/lifetime validation. No signing key exists in production code.

## Automated evidence

**65 tests passed**, zero failed/skipped: original 22 plus 43 Phase 2 cases.

- Profiles: HTTPS and explicit literal-loopback h2c; remote HTTP, credentials/query, confusing host/path, invalid ID, managed JSON/unknown credential fields rejected.
- OIDC: full code+S256 exchange through an ephemeral in-memory HTTPS protocol handler, discovered signed public JWKS and RS256 ID token, real TCP loopback callback. Wrong ID issuer/audience/nonce/signature/lifetime, bad code, discovery issuer and unsupported PKCE rejected. The fixture checks the actual verifier/challenge and redirect binding, public client/no secret, and access-token/ID-token separation.
- Callback: loopback binding/unique ports, wrong path/state rejection, one matching callback, replay rejection, cancellation/timeout and closed socket.
- Session: local logout clears identity/expiry/data, cancels/disposes backend and suppresses late reads; profile switch suppresses late identity; restart starts signed out; expiry clears material. Denial/outage preserve identity; rejected credentials clear it.
- Transport: generated production clients against an ephemeral Kestrel HTTP/2 endpoint with real protobuf gRPC framing/trailers. Ping has no auth; GetRaceDay/ListRaces/GetRace each carry exactly one access-token bearer. Tests assert HTTP/2 and grpc-timeout metadata, actual deadline expiry and cancellation, event scoping, server phase/version/runner identifiers and distinct status codes. A redirect target receives no request/bearer.
- No secret persistence or logging code is introduced (source inspection); there is no forensic memory/dump guarantee or independent exhaustive log/disk instrumentation test.

Restore, Debug/Release x64 solution builds, dotnet test, four contract hashes and git diff --check passed locally. Solution builds reported zero warnings/errors. Unsigned Release MSIX succeeded; the existing missing mspdbcmf.exe warning prevents a symbols package only. No signing or publishing occurred. The exact reproducible commands remain in README; builds use -m:1 --disable-build-servers.

Early test attempts found a browser-fixture receive ordering deadlock and a JWKS conversion using ToString instead of RawData. Both were corrected before the passing suite. An early capture raced app startup; a repeated dotnet run encountered the known in-use apphost lock. Closing only our preview before rebuild resolves it; no user data was removed.

## Native smoke

Real packaged development-identity launch used the existing WinApp dotnet run route. First invocation continues after terminal collection timeout; allow startup before automation. Final unconfigured native evidence is local in **.awb/phase2-final-native/**: 24 PNGs (Cashier, Administration, Settings; Light/Dark; 584x746, 930x746, 1440x746, 1920x1080). Corresponding UI Automation logs are .awb/phase2-final-*.log. DPI 96/100%. Screenshots are deliberately uncommitted and contain no private account data.

Rendered inspection of narrow Light Settings and fresh wide Cashier confirms shared title/gutters, no overlapping header, compact unconfigured controls, disabled connection/read actions without profile, disabled transactions and clearly fictional fixture cards. The initial capture showed blank TextBlock spacing; final code collapses empty fields. .awb/phase2-probe/fresh-wide.png shows expanded labeled wide navigation before capture automation; matrix captures include navigation/resize history and may show a manually collapsed pane. No resize policy was changed. Existing controls/themes/transitions remain native.

This is **unconfigured layout/navigation smoke**, not authenticated packaged acceptance. No managed provider profile was present. System-browser launch/return in packaged context, signed-in/denied/offline visual states, authorized SERVER data and subject switching in the actual native app are **unverified**. Advanced accessibility, high contrast, non-100% DPI, clean-machine install and Rider debugger remain unverified/deferred.

## External provider and completion gates

Owner confirmed no approved external environment is available. No real provider or actual Tiket backend was authenticated or queried. The synthetic OIDC fixture uses an injected in-process HTTP handler, not a real HTTPS issuer deployment; gRPC wire integration uses a separate fake server. These establish protocol/transport behavior only, not production-provider compatibility or backend security compatibility.

Phase 2 implementation is reviewable, but packaged authenticated acceptance gates remain open. Do not claim the phase operationally complete. Follow CONNECTION_AND_OIDC.md with an owner-provisioned native/public client, trusted HTTPS issuer/backend and known authorized/out-of-scope events. Record issuer versions, browser callback, API reads, denial and logout before accepting those gates. Windows CI status must be checked on the final PR commit; the PR and local successor handoff contain the exact run/head links.

Phase 3 is not started. It requires owner acceptance of these remaining gates and a separate scope for read-only quote integration; no financial/admin mutation, print or release is enabled here.
