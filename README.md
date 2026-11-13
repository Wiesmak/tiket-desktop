# Tiket Desktop

Phase 5 adds a native multi-race local wizard, encrypted Save/Resume, immutable day creation, lifecycle controls and scoped enrollment/opening funding. Coordinated automatic reads keep cashier, account and print state current while preserving unchanged quotes. Phase 4 purchase/reprint/recovery stays enabled under normal verified grants and journal safety. Real disposable-lab creation, onboarding, one purchase/reprint and committed/lost-ACK/fresh-native recovery passed. See [Phase 5 validation](docs/VALIDATION_PHASE_5.md), [cashier guide](docs/CASHIER_QUOTES.md), [journal/recovery](docs/SAFE_PURCHASE_JOURNAL.md) and [connection setup](docs/CONNECTION_AND_OIDC.md). Signed distribution, physical paper and shared-workstation deployment remain separate work.

## Build and run

Windows 11 x64 (build 22000+) with .NET SDK 11.0.100, Windows SDK tooling, and Rider. The UI targets `net11.0-windows10.0.26100.0`; keep the existing `Tiket Desktop.slnx` and project path. Open that solution directly in Rider. NuGet access is needed on first restore. No separate solution or template generation is needed.

Run from the repository root in Rider's terminal:

```powershell
dotnet --info
dotnet restore "Tiket Desktop.slnx" -p:Platform=x64
dotnet build "Tiket Desktop.slnx" -c Debug -p:Platform=x64
dotnet test "Tiket.Desktop.Tests/Tiket.Desktop.Tests.csproj" -c Debug
powershell -NoProfile -File scripts/check-contracts.ps1
dotnet run --project "Tiket Desktop/Tiket Desktop.csproj" -c Debug -p:Platform=x64 --no-launch-profile
dotnet build "Tiket Desktop.slnx" -c Release -p:Platform=x64
dotnet build "Tiket Desktop/Tiket Desktop.csproj" -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never
```

The verified run path uses the existing WinApp integration to register a development identity and launch by AUMID. It leaves the GUI running, so the terminal tool may time out after successful launch. Requires Windows development registration support. It does not require a newly generated certificate. No certificate was created or committed for this work. Set `WINAPP_CLI_TELEMETRY_OPTOUT=1` to opt out of WinApp CLI telemetry. Rider's Package profile failed locally with “Process handler is null even though RunContentDescriptor exists”; direct Rider debugging and XAML Hot Reload are not verified. Use the terminal run path above. Visual Studio is optional for external troubleshooting.

## Pinned dependencies

| Dependency | Version |
| --- | --- |
| .NET SDK | 11.0.100 (latest patch roll-forward) |
| Microsoft.WindowsAppSDK | 2.6.5 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2957 |
| Microsoft.Windows.SDK.BuildTools.WinApp | 0.8.0 |
| Duende.IdentityModel.OidcClient (Apache-2.0) | 7.1.1 |
| Microsoft.IdentityModel.JsonWebTokens (MIT) | 8.23.0 |
| Google.Protobuf | 3.33.1 |
| Grpc.Tools / Grpc.Net.Client | 2.76.0 |
| bundled protoc | 31.1 |
| Microsoft.NET.Test.Sdk | 17.14.1 |
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.5 |

The three Windows package pins predate Phase 0 and are preserved. All restored and compiled locally; these version strings have no prerelease suffix. WinApp has its own 0.x version line; no invented 2.6.5 version is applied to it. This is compatibility evidence for this checkout, not a promise about newer releases. Transitive dependencies are resolved by NuGet; direct versions are explicit.

## Contracts and design

Four exact upstream protobuf files are committed under `contracts/upstream`, at backend SHA `d840ab12a08b06304b5e4d26568ba33e98a65ddc`. [Snapshot manifest](contracts/snapshot.json) records SHA-256 digests and generator versions. Builds use only local contracts after restore; generated client-only C# stays in obj. Run `powershell -NoProfile -File scripts/check-contracts.ps1 -Upstream` to compare upstream without replacing files.

Read [architecture](docs/ARCHITECTURE.md), [UX](docs/UX_SPEC.md), [security](docs/IDENTITY_AND_SECURITY.md), [contract gaps](docs/BACKEND_CONTRACT_GAPS.md), [decisions](docs/DECISIONS.md), [roadmap](docs/ROADMAP.md), [historical Phase 0 validation](docs/VALIDATION.md) and [Phase 1 evidence](docs/VALIDATION_PHASE_1.md). Workspace columns start at 900 available effective pixels with capped primary inputs. Narrow views stack live local history below the editor; purchase/UNKNOWN feedback remains near the top. MOCK cashier receipts have been removed.
Backend [API](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/overview.md), [permissions](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/authentication.md) and [replay](https://github.com/Wiesmak/tiket/blob/d840ab12a08b06304b5e4d26568ba33e98a65ddc/docs/api/errors-and-idempotency.md) remain authoritative.

Design follows [Windows guidance](https://learn.microsoft.com/en-us/windows/apps/design/), [NavigationView](https://learn.microsoft.com/en-us/windows/apps/design/controls/navigationview) and [keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility). CI builds/tests on Windows x64; it does not sign, release, authenticate or drive hardware. Local UI tests remain required.

Existing Apache 2.0 LICENSE.txt and assets are preserved. No upstream artwork or branding is imported. Asset ownership still needs owner review.
