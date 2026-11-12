# Phase 3 evidence — 2026-11-12

## Baseline and scope

Verified current GitHub status rather than archival handoff status: Phase 2 PR #3 is merged at **98ffa6d4e58883129a7b352c9fb1c19077b9a594**. Final-head Windows run [43241584459](https://github.com/Wiesmak/tiket-desktop/actions/runs/43241584459) and post-merge run [43245964408](https://github.com/Wiesmak/tiket-desktop/actions/runs/43245964408) succeeded. Fetched origin and created feat/phase3-cashier-quote from that exact origin/master. Only untracked .awb/ existed; preserved. Owner requested .awb/ be ignored; .gitignore now excludes local evidence/reference checkouts.

Read repository AGENTS/README/architecture/security/UX/gaps/connection/Phase 2 validation/roadmap, the Downloads review handoff including its original archival handoff, pinned schemas and backend betting rules/cashier/error guidance/Selections.kt/BettingApplicationService.kt in the existing read-only reference checkout. The Downloads file was outside Rider source roots and was read through Rider terminal. Backend ls-remote and check-contracts.ps1 -Upstream both verified unchanged **d840ab12a08b06304b5e4d26568ba33e98a65ddc**. All four hashes match; no protocol files regenerated into Git.

Existing four projects, .NET 11 SDK 11.0.100, WinUI/Windows packages, package identity/publisher/assets/license and shell layout are preserved. **No new dependencies or version changes.** No mutation, purchase/reprint/journal/printing, signing, publishing workflow change or Phase 4 work.

## Automated checks

**133 tests passed, zero failed/skipped: original 65 plus 68 Phase 3 cases.** Phase 3 tests cover all eleven valid pool/mode pairings: WIN Normal/Box and QUINELLA/EXACTA/TRIFECTA Normal/Wall/Box. Tests verify exact serialized generated requests, positional order, stable UUIDs and separate protobuf variants; no client expansion engine exists.

Coverage includes positive int64 values through long.MaxValue and above double's exact range; overflow/blanks/signs/non-ASCII digits/fractions/long text; box minima including WIN; duplicate/unknown/scratched explicit IDs, all-wall/WIN-wall and wrong arity; leading-zero labels; 2/12/32-runner model contexts; closed day/race; authoritative response cloning, differing canonical order and integer monetary values; completed-quote invalidation and delayed-result suppression after edits, refresh/scratch, race/day/profile changes, sign-out/expiry, cancellation, rapid edits and shutdown.

The Kestrel HTTP/2 fixture exercises the actual generated BettingService/QuoteCoupon path, exact protobuf request, one Bearer access token, grpc-timeout/HTTP2 metadata, real deadline/cancellation and distinct sanitized statuses (including INVALID_ARGUMENT, FAILED_PRECONDITION and RESOURCE_EXHAUSTED). Session tests link editor and lifetime cancellation. Fixtures supply server output with independent values and versions; they do not prove Tiket pricing, expansion, authorization or settlement logic.

Debug and Release x64 builds, unsigned Release MSIX, whitespace and pinned direct-version checks passed on final source. The MSIX symbols-only missing mspdbcmf.exe warning is historical. Rider inspections of MainPage.Quote.cs, MainPage.xaml, CashierComposer.cs and ConnectionSession.cs reported no errors. Early compilation caught the WinUI SelectionMode naming collision, corrected using a local alias. A repeated native launch hit the known in-use apphost lock; closing our preview resolved it.

## Native evidence

Packaged development-identity launch used the documented Rider terminal dotnet run route. It continues beyond terminal collection timeout; allow startup before automation. DPI 96 / 100%. No real account or access token is in captures.

- **.awb/phase3-final-disconnected/**: final production-source Cashier at 584×746, 930×746, 1440×746 and 1920×1080 in Light/Dark. Matching .awb/phase3-final-Cashier-*.log UIA logs. Disconnected inputs are collapsed; guidance/disabled quote/purchase and MOCK print/history remain.
- **.awb/phase3-native-fixture/**: eight corresponding native editor captures using a temporary injected synthetic session/read/quote service with 32 runners and conspicuous SYNTHETIC UI FIXTURE banner. Normal Trifecta, separate ordered choices, leading zeroes and returned quote values persist through theme/navigation/resize. Matching .awb/phase3-fixture-Cashier-*.log logs.
- **.awb/phase3-wall/** and .awb/phase3-wall.log: mixed explicit/WALL native selection/summary.
- **.awb/phase3-box/** and **.awb/phase3-box-selected/** with .awb/phase3-box-selected.log: bounded 32-runner box and selected three-horse quotation.
- **.awb/phase3-keyboard/**: native keyboard smoke moved from units to Get quote and through native navigation. Matrix sequences resize narrow→wide in both themes and retain quote/draft; manually collapsed pane state is visible in wide captures. Explicit native pane automation also verified expansion, manual collapse persisting through narrow→wide resize, and expansion again. This is not full accessibility certification.

The capture-only NativeVisualFixture source is retained solely under ignored .awb/ for provenance. The temporary application file and session injection/banner were removed, production-source Debug rebuilt, and source search found no NativeVisual reference. There is no shipped fixture login bypass or demo-to-live quote path. Native visual fixtures use an injected fake service, not HTTP/2 or a real provider; wire tests independently establish transport. Evidence must never be presented as real authenticated acceptance.

Early captures in .awb/phase3-native-disconnected show empty selectors and are superseded by final-disconnected. Automation initially looked for obsolete demo input and then failed to find unrealized box rows; corrected keyboard target/selection-by-container captured the final states. A blocking Rider dialog delayed tools; owner closed it. No evidence was deleted.

## Integration gates and limitations

**Real OIDC packaged smoke unverified.** No approved trusted external provider/native registration/backend profile supplied. The deferred Phase 2 browser callback, authorized known-event read, out-of-scope denial, expiry/logout/operator/profile crossover acceptance still must follow CONNECTION_AND_OIDC.md.

**Real Tiket quote unverified.** No disposable scoped user and RUNNING/BETTING_OPEN trusted backend supplied. Synthetic native/wire fixtures do not close this gate. Quote requires backend CASHIER or PLAYER authorization and resource scope; overview navigation is not permission enforcement.

No push status exists; quote freshness is only a timestamp and any quote can become unusable immediately. Server eligibility/expansion limits remain final authority. Narrow Trifecta requires vertical scrolling; native combo popups and bounded Box keep target sizes readable. Full Narrator/high contrast/text scaling/multi-DPI/clean signed installation and operational cashier speed remain deferred. Phase 4 must not be enabled until real identity/transport acceptance and durable exact-intent/recovery design are reviewed.

Final commit/PR/CI and cumulative successor handoff are recorded in the delivery report and local .awb/TIKET_DESKTOP_HANDOFF_AFTER_PHASE_3_2026-11-12.md.
