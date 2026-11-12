# Phase 1 local evidence — 2026-11-12

## Prerequisite and environment

Started from current master **8b0ec578390374d3b1560028d894c7cc74440792**, independently checked against origin with git ls-remote. PR #1 is merged; final head 61bf8dc35e9c1b5081353312efd006b4c3fe9809 passed Windows CI. The merge also passed [run 43217358734](https://github.com/Wiesmak/tiket-desktop/actions/runs/43217358734). PublishTrimmed=false was already corrected before Phase 1. No prerequisite fix is hidden here.

Created feat/phase1-shell-ux. Initial local status had only untracked .awb/, preserved without reset/overwrite. Read all eight required repository documents, AGENTS.md and the owner's Downloads handoff; the latter required Rider terminal reading because Rider read_file rejects paths outside its source roots.

Windows 11 x64 10.0.26200, Rider 2026.3 session, SDK 11.0.100 (297acf930f), MSBuild 18.12.2. WindowsAppSDK 2.6.5, BuildTools 10.0.28000.2957 and WinApp 0.8.0 preserved. No direct dependencies, identity, bootstrap, assets, protobuf bytes or backend behavior changed.

## Build and boundary checks

- dotnet restore "Tiket Desktop.slnx" -p:Platform=x64: success.
- Debug and Release solution builds, x64 with -m:1 --disable-build-servers: success, **zero warnings/errors** on final source.
- Debug unit tests: **22 passed**, none failed/skipped (existing 11 plus 11 cases for effective-width boundary decisions, mock status labels, step descriptions, session appearance and repeated navigation). No WinUI activation in tests.
- scripts/check-contracts.ps1: all four hashes match.
- Unsigned Release MSIX: success. Same Phase 0 local tool warning: mspdbcmf.exe missing, so no symbols package. No signing credentials used.
- Rider diagnostics on MainPage XAML/code-behind and presentation policy: no errors.
- git diff --check: passes; Git may print local LF/CRLF conversion notices.
- Hosted Phase 1 results are attached to the PR checks, with a specific run link in its description and the successor handoff. This local report does not substitute for hosted CI.

Real packaged CLI launch used:
dotnet run --project "Tiket Desktop/Tiket Desktop.csproj" -c Debug -p:Platform=x64 --no-launch-profile

The GUI remains running beyond the terminal timeout. A repeated run while it was open hit MSB3027/MSB3021 trying to copy the in-use apphost. Closing only our preview window, rebuilding and launching succeeded. An early Release invocation exceeded the terminal collection timeout; a subsequent completed build verified success. These are local preview/tool limitations, not ignored build failures. Rider debugging and XAML Hot Reload were not tested and remain unverified; no claim of fixing the Phase 0 Package-profile limitation.

## Native UI coverage

Final screenshots and automation logs are local under **D:/source/Tiket Desktop/.awb/phase1-validated/**, deliberately excluded from commits. File pattern: Destination-Theme-Width-Height-step2.png. Destination is Cashier, Administration or Settings; theme is Dark or Light. Administration step2 is Runners. Extra step0/1/3/4 screenshots cover Basics, Races, Pool rules and Review. Earlier exploratory captures in phase1-review/phase1-final are superseded.

| Actual outer window (effective pixels at 96 DPI) | Cashier | Administration | Settings |
| --- | --- | --- | --- |
| 584 x 746 | Dark + Light; latest mock summary above inputs | Dark + Light; warning and roster visible, summary stacks | Dark + Light; reachable via minimal pane |
| 930 x 746 | Dark + Light; latest summary, full list below | Dark + Light; step/roster/summary stack | Dark + Light; compact rail |
| 1440 x 746 | Dark + Light; editor/list columns | Dark + Light; builder/summary columns | Dark + Light |
| 1920 x 1080 | Dark + Light; 1200-pixel content cap | Dark + Light; bounded composition | Dark + Light |

Capture logs record requested and actual dimensions, DPI **96 / 100%**, theme and enabled states. These are outer-window dimensions, not claims that the form itself has that width. No 125% scaling was configured. Native Auto navigation was exercised in minimal, compact, expanded, and manually collapsed wide modes. Resizing in both directions and repeated Settings returns preserved existing controls and temporary inputs.

Visual review found no hamburger/header overlap, vertical footer text, zero-width/clipped inputs or horizontal scrolling at these sizes. At narrow sizes the full recent list and secondary administration content require vertical scrolling; latest cashier status and the immutable-DRAFT warning do not. The WinApp DevTools chip visible in Debug captures is tooling, not a product control.

Light over OS-dark Mica initially exposed a contrast defect; a theme-resource page surface corrected it. Final captures use the corrected surface. Native title bar/system controls retain OS styling. System appearance was selected again in a separate narrow capture under .awb/phase1-keyboard-final/. OS-wide theme changes were not automated, so dynamic OS theme-switch certification remains unverified.

## Keyboard and focus evidence

A guarded local script (.awb/phase1-keyboard.ps1) checks the exact app foreground handle before every SendKeys call. Starting from Cashier at 584 x 746 it passed:

- Space opens the focused native menu; Escape restores Open Navigation focus.
- With pane open, Alt+A / Alt+C change destination; the destination header receives focus after pane dismissal.
- Tab moves player -> bet type -> selection mode -> Runner A. Shift+Tab reverses.
- Space/Down/Enter choose a bet type; Space toggles Runner A and restores it.
- The builder combo accepts Space/Home/Down/Down/Enter for Runners.
- Tab reaches Runner A program then display name; Shift+Tab returns to program, whose value remains the string "02".

Access keys on hidden minimal-pane items require opening the menu first; no global keyboard hook was added. Selecting an already-selected destination follows native dismissal behavior and does not count as a destination change. Appearance changes do not recreate controls or intentionally move focus. Full assistive-technology behavior is not certified.

Narrator, high contrast, full text scaling, actual 125% and multi-DPI, signed clean-machine install, Rider debugger and Hot Reload remain deferred/unverified. No authentication, live gRPC, financial mutation, price calculation, journal, printer access, release or Phase 2 work was performed.

## Owner review correction — 2026-11-12 (supersedes visual acceptance above)

The owner rejected the composition at d8d3205 despite its green checks. The earlier results remain historical, not approval. Corrective source keeps native Auto navigation and guards the premature initial-selection pane close with IsLoaded. It shares title/status/body alignment, removes the global 1200 cap, caps primary columns and shortens mock messaging. No business command is enabled.

Corrected packaged-app evidence is under .awb/phase1-corrected/matrix/ (36 PNGs and corresponding logs): Cashier, Administration (Runners), Settings; Light and Dark; actual outer bounds 584x746, 930x746, 1084x746, 1440x746, 1680x900, 1920x1080. Logs record 96 DPI / 100%. scripts/inspect-ui.ps1 selects theme/destination and therefore these are resize-history captures, not launch evidence. Separate sequences/ captures retain fresh-process first sizing, forward/reverse resizing and manual toggle evidence without theme/destination changes before each snapshot. The first window is created at its native default size then immediately sized for fresh-process captures; exact startup construction at those requested dimensions is not claimed.

Rendered inspection found the shared 28px title and aligned gutters, expanded labeled wide navigation, separate minimal hamburger band, no horizontal overflow and bounded inputs. Minimal views intentionally scroll the lower summary/history; latest mock print precedes inputs. Short mock pages retain empty space below content. Debug DevTools and OS-styled title bar remain tooling/native surfaces. Owner screenshot approval is pending; these checks do not certify accessibility or production readiness.

Restore, x64 Release build (zero warnings/errors), 22 xUnit tests and all four contract checks passed during correction. The unchanged pure 900-pixel breakpoint tests remain applicable; no new pure breakpoint or navigation policy was introduced (the fix guards WinUI lifecycle orchestration). Final checks and hosted CI are recorded in the successor local handoff.

Final correction checks: Debug and Release x64 solution builds passed with zero warnings/errors; unsigned Release MSIX passed with the historical missing-mspdbcmf symbols warning; Rider reports no errors in the three changed source files; diff --check passes. Foreground-guarded keyboard and session-state scripts passed again after isolating them from capture automation. An earlier overlapping automation run failed its focus precondition and is not acceptance evidence. Separate clean-* and collapsed-* sequence captures supersede the overlapping manual-* captures. Native Auto reopens on return wide without explicit collapse; explicit manual collapse survives wide resize, navigation, theme switching and narrow→wide until reopened by the toggle. Fresh-process screenshots show 212px navigation items (220px pane), compared with 40px icon items after collapse.

## Workspace transitions — 2026-11-12

Added native FadeInThemeAnimation plus RepositionThemeAnimation (16 effective pixels vertically) for changed destinations and builder steps. Existing controls remain in place; initial construction, resizing and theme changes do not start the animation. Each change stops the previous storyboard and checks UISettings.AnimationsEnabled. No animation delay gates navigation, input or focus.

Debug/Release x64 builds passed with zero warnings/errors, all 22 tests passed, Rider source diagnostics and diff --check passed. Packaged app was launched and foreground-guarded keyboard/session scripts passed again (including input preservation and appearance focus). Timed rendered frame sequences for repeated Cashier/Administration/Settings changes are local under .awb/phase1-transitions/dark and light at 1440x746. Intermediate and settled frames show the native vertical entrance. Windows Animation effects preference was not changed during this check; the disabled-motion code path is inspected, not claimed visually tested. No pure Application policy changed; no rendering claims derive from unit tests.
