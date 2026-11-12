# Phase 0 local evidence — 2026-11-12

Implementation is reviewable on feat/phase0-foundation. Full acceptance is still pending the explicitly unverified UI/CI checks below.

## Starting point and environment

- Starting branch master; live HEAD 02d071d6da11cd46b43fc1e61836d31d4f1dfc49. Owner confirmed this newer baseline. Handoff 8b4bf0f was not checked out or restored.
- Initial status: only untracked .awb/; no tracked/staged changes. Existing .awb/session-mcp content preserved. No reset, force push, template generation or upstream mutation.
- Windows 11 x64, OS 10.0.26200; active display DPI 96 (100%).
- .NET SDK 11.0.100, commit 297acf930f; MSBuild 18.12.2-1.26529.118; .NET runtime 11.0.0. SDK 9.0.320 also installed; no workloads required for this project.
- Running Rider binary: C:/Users/Gerard/AppData/Local/Programs/Rider/bin/rider64.exe, product version 263.7307.258.0-RD. Rider 2026.3 session. Solution-project discovery lists UI, Application, Contracts and Tests after edits.
- Windows App SDK 2.6.5, SDK BuildTools 10.0.28000.2957, WinApp package/CLI 0.8.0 preserved and resolved locally. Package versions and exact commands are in README.
- Owner's inception ZIP read at C:/Users/Gerard/Downloads/Tiket_Desktop_Architecture_Starter_2026-11-12.zip. Six design documents and both conceptual PNGs reviewed; obsolete project-creation assumptions reconciled.

## Results

| Check | Local result |
| --- | --- |
| dotnet --info | Success; versions above |
| Restore solution x64 | Success, all four projects |
| Untouched template Debug x64 | Success, zero warnings/errors |
| Final Debug solution x64 | Success, zero warnings/errors using serial equivalent in README note below |
| Final Release solution x64 | Success, zero warnings/errors; trimming ran |
| Unit tests | 11 passed, zero failed/skipped; navigation notification/destination validation, permanently disabled submission, HTTPS/loopback configuration cases, Ping client/message compilation and checksum/protocol shape |
| Snapshot check including upstream | Success after fetching exact original bytes; upstream master unchanged at reviewed pin |
| Native packaged launch | Success through dotnet run / WinApp, responsive window and automation tree |
| Rider Package launch profile | Failed: Process handler is null even though RunContentDescriptor exists |
| Unsigned Release MSIX | Success; warning: mspdbcmf.exe absent, therefore symbols package omitted |
| IDE diagnostics | Rider Problems View returned no warnings/errors |
| Whitespace | git diff --check passed |
| Hosted GitHub Actions | Workflow added; not pushed or executed, no claimed green hosted run |

One later Debug build hit WMC9999 because XamlSaveStateFile.xml was locked by another process while the preview/toolchain was active. Closing the launched preview and running serially succeeded:

```powershell
dotnet build "Tiket Desktop.slnx" -c Debug -p:Platform=x64 -m:1 --disable-build-servers
dotnet build "Tiket Desktop.slnx" -c Release -p:Platform=x64 -m:1 --disable-build-servers
```

The launch command registered/used package identity 663d5b9a-e6ff-4a09-9df9-80df8da3b473_0c4xq9r3xq73r!App. WinApp verified compiled XAML. Initial PID 37880 and later launch PID 14036 were observed. The terminal times out while the GUI stays open; this is not a launch failure. This verifies running, not Rider debugging. Hot Reload and debugger breakpoints are unverified.

## UI evidence and limits

Native UI Automation selected Cashier, Administration and Settings. It exposed labelled fields, headings and disabled quote/purchase/create/connect actions. Four Tab presses from player input focused bet type, selection mode, Runner A and Runner B in that order. No backend RPC was sent. The optional keyboard script now aborts if the app loses foreground so it cannot type into another program.

Default system appearance was dark. Dark cashier at 1366x768 and admin at 1920x1080 were visually inspected. Appearance switching to Light and Dark worked through the native combo. Screenshot capture uses PrintWindow to isolate the app from other desktop windows. scripts/inspect-ui.ps1 records requested dimensions, actual DPI and automation labels; captures stay under untracked .awb/. They are local evidence, not baseline tests. A smaller 1093x614 effective window was also exercised as a layout probe, not proof of actual 125% scaling.

Outstanding acceptance checks: actual 1366x768 at 125% Windows scaling, Windows high contrast, Narrator, complete keyboard-only traversal/step selection, text scaling and a clean-machine install of a signed package. Built-in controls, automation names and theme resources are implemented, but high-contrast/Narrator support is not certified by these checks. Windows minimum 22000 was aligned deliberately; actual host validation was on 26200 only. No supported x86/ARM64 release is claimed.

CI is Windows x64 with SHA-pinned checkout/setup-dotnet and read-only contents permission. It restores/builds/tests, validates local contract hashes and direct dependency pins, and generates an unsigned package. No sign-in, hardware, signing credential, release, push or hosted GUI test is configured.

## Contract provenance

Chosen upstream SHA: d840ab12a08b06304b5e4d26568ba33e98a65ddc, identical to reviewed baseline and live master at inspection. Manifest: contracts/snapshot.json. Grpc.Tools 2.76.0 reports libprotoc 31.1; generated Client-only code compiles all eight services / 37 methods.

| File under pl/umamusume/tiket/api/v1 | SHA-256 |
| --- | --- |
| account.proto | ad524e412ae4d43a02b414bccaf0cba1404d1102380d76f45f21a91c6da4b3d4 |
| administration.proto | 7adaeb0ba42ccd9c88752f4a751df433f4d9a87a47aa06a7c3da60f4584252f2 |
| betting.proto | c400ffc821b2ca053331528fa8cf73421175379c8c4c7afa5ed7599b691077e8 |
| system.proto | b2dcdc1d2041ffb5f4dda30cd8e93b89e579821fef30c6db94f4ebd9daa52009 |

The IDE initially added a BOM/trailing blank line to new .proto files. The upstream comparison caught it. Original pinned raw bytes were then downloaded, checksums updated, and the upstream check plus unit test rerun successfully. .gitattributes keeps protobuf LF stable across checkouts.

## File inventory

Modified: .gitattributes (protobuf LF rule); Tiket Desktop.slnx (x64 and three projects); Tiket Desktop/Tiket Desktop.csproj (references and Windows minimum/x64 alignment); Tiket Desktop/Package.appxmanifest (desktop minimum); Tiket Desktop/MainPage.xaml and MainPage.xaml.cs (native shell).

Added: global.json; README.md; Tiket.Desktop.Application/{Tiket.Desktop.Application.csproj,ShellState.cs}; Tiket.Desktop.Contracts/Tiket.Desktop.Contracts.csproj; Tiket.Desktop.Tests/{Tiket.Desktop.Tests.csproj,FoundationTests.cs}; contracts/snapshot.json and four original contracts/upstream/pl/umamusume/tiket/api/v1/*.proto; scripts/{check-contracts.ps1,inspect-ui.ps1}; .github/workflows/windows.yml; docs/{ARCHITECTURE,UX_SPEC,IDENTITY_AND_SECURITY,BACKEND_CONTRACT_GAPS,DECISIONS,ROADMAP,VALIDATION}.md.

App/MainWindow bootstrap, title bar, MicaBackdrop, RootFrame, application manifest, publisher identity, package name, assets, license and existing ignore rules are preserved. No new art, credentials, certificates, authentication, financial behavior, journal or printer integration is added.
