# Phase 0 decisions

1. Preserve the existing .slnx, UI project/path/namespace, XAML bootstrap, packaging identity, license and committed assets. Start from live master 02d071d6da11cd46b43fc1e61836d31d4f1dfc49, not obsolete inception assumptions.
2. Retain .NET 11 and all owner-pinned Windows packages. Use the installed SDK 11.0.100 with patch roll-forward. No second UI TFM.
3. Windows 11 x64 first: align minimum manifest/compiler/supported OS to 10.0.22000.0; retain target SDK 26100 and record tested host 26200. Remove solution architecture advertisements for untested x86/ARM64; preserve historical publish-profile files as template artifacts, not supported release targets.
4. Two reusable libraries and one test project suffice. Plain INotifyPropertyChanged supports the tiny navigation model without an additional MVVM dependency. Future use cases can justify CommunityToolkit.Mvvm.
5. Generate Client only from byte-exact local contract snapshot. Grpc.Tools 2.76.0 bundles protoc 31.1. No runtime upstream fetch or reflection.
6. Retain Release PublishTrimmed=true because the actual Release compile and unsigned package build succeed without trim/XAML warnings. This does not prove all future reflective bindings safe: re-evaluate as real features arrive. No blind trimming workaround is needed for this shell.
7. Preserve the publisher; do not sign or publish. Native run uses the existing WinApp 0.8.0 development identity integration. runFullTrust is the sole declared capability; AI capability was already removed at starting HEAD.
8. Mock UI only. Browser PKCE identity, exact-command journal, business adapters, persistence and all hardware interactions remain deferred. Never treat disabled UI as backend authorization.
