# Phase 0 decisions

1. Preserve the existing .slnx, UI project/path/namespace, XAML bootstrap, packaging identity, license and committed assets. Start from live master 02d071d6da11cd46b43fc1e61836d31d4f1dfc49, not obsolete inception assumptions.
2. Retain .NET 11 and all owner-pinned Windows packages. Use the installed SDK 11.0.100 with patch roll-forward. No second UI TFM.
3. Windows 11 x64 first: align minimum manifest/compiler/supported OS to 10.0.22000.0; retain target SDK 26100 and record tested host 26200. Remove solution architecture advertisements for untested x86/ARM64; preserve historical publish-profile files as template artifacts, not supported release targets.
4. Two reusable libraries and one test project suffice. Plain INotifyPropertyChanged supports the tiny navigation model without an additional MVVM dependency. Future use cases can justify CommunityToolkit.Mvvm.
5. Generate Client only from byte-exact local contract snapshot. Grpc.Tools 2.76.0 bundles protoc 31.1. No runtime upstream fetch or reflection.
6. Superseded before Phase 0 merge: ordinary Release builds use PublishTrimmed=false. Hosted CI exposed NETSDK1102 with the original trimming configuration. PR #1 final head 61bf8dc corrected it and merged as 8b0ec578 with green Windows CI. Trimming experiments require a separate validated publish configuration.
7. Preserve the publisher; do not sign or publish. Native run uses the existing WinApp 0.8.0 development identity integration. runFullTrust is the sole declared capability; AI capability was already removed at starting HEAD.
8. Mock UI only. Browser PKCE identity, exact-command journal, business adapters, persistence and all hardware interactions remain deferred. Never treat disabled UI as backend authorization.

## Phase 1 decisions

9. Use the native NavigationView.Header for destination identity and remove arbitrary pane-footer text. Preserve the MainWindow title bar, Mica and package identity.
10. Use measured workspace width (900 effective pixels), XAML layout setters and a 1200-pixel cap. A view-only SizeChanged bridge selects the pure layout policy; no business state or manual grid relocation lives there.
11. Keep the fictional latest-print summary above the editor at every width. No enabled reprint or claimed POS-wide history.
12. Five mock builder steps reveal distinct temporary content; roster programs remain TextBox strings. No editable backend DRAFT, persistence or creation.
13. Appearance stays session-only, defaults to System and needs no Windows storage interface in Phase 1. Native theme resources provide the page surface even when a forced theme differs from OS Mica.
14. Basic keyboard/focus and theme checks are Phase 1 requirements. Narrator/high contrast, full text scaling and multi-DPI certification belong to Phase 8.

## Phase 1 corrective review — 2026-11-12

15. Supersedes decisions 9–10: move the 28/36 native title into the same uncapped workspace as status and content. Use 24/12 horizontal gutters and a separate native minimal-menu band; retain the 900-content-pixel breakpoint, use 24-pixel column spacing and cap only primary columns (cashier 680, builder 760). No fake content fills the remaining vertical space.
16. Guard initial navigation with IsLoaded before overlay dismissal. Pre-layout DisplayMode is not a reliable reason to close a default pane. Preserve native Auto thresholds, manual wide collapse across resize cycles; an uncollapsed Auto pane expands again on returning wide. No persistent preference or per-resize forced opening.
17. Owner rejected the earlier visual composition despite green CI. Historical validation below the original head is retained; corrected screenshots require owner review before merge.
