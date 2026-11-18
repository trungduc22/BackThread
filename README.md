# BackThread

BackThread connects a Windows 10 computer to an iPhone over Lightning USB for PCM audio, media controls, notifications, and independent file jobs.

**Current state:** the Windows Host and protocol simulator remain working. The native Agent and UIKit app now compile, link and package as ARM64 for iOS 12.0 using an actual iPhoneOS 12.4 SDK. Two ad-hoc-signed Debian packages and a test IPA are available. The physical iPhone 6 / iOS 12.5.8 is paired and reachable through Dropbear SSH on USB port 44. Device System Policy denied standalone execution, and Apple's installer rejected the app signature (`0xe8008014`). **No native BackThread code has run on the phone and no iPhone speaker output is verified.** Read [feature status](docs/FEATURE_STATUS.md) and [hardware results](docs/HARDWARE_TEST_RESULTS.md).

Implemented Windows components include authenticated usbmux and localhost transport, WASAPI loopback, GSMTC media controls, bounded event routing, resumable binary transfers, SHA-256 verification, text and metadata jobs, an authenticated local integration API, and a WinForms tray. The simulator executes actual jobs and receives actual PCM frames; it does not play through an iPhone speaker.

## Build and test

Use a .NET 8 SDK on Windows 10 1809 or later; builds use Windows SDK contracts 19041. GCC is optional for the shared C tests.

```powershell
dotnet build BackThread.sln -c Release
./scripts/test.ps1
./scripts/test.ps1 -SoakSeconds 120
python ./scripts/diagnose-hardware.py --output artifacts/hardware-audit.json
python ./scripts/inspect-ios-device.py --output artifacts/ios-device-audit.json
dotnet run --project src/Windows/Host -- --diagnose
```

The test runner is an executable with a nonzero failure exit code; use the script or `dotnet run --project tests/BackThread.Tests -c Release`, not `dotnet test`.

## Run with the simulator

From the repository root:

```powershell
dotnet run --project src/Windows/Host -- --init-key config/session.key
Copy-Item config/example.json config/local.json
# Edit config/local.json: set transport to "localhost".
# Set fixtureAudio to true for generated audio, or leave false for actual WASAPI capture.
dotnet run --project src/Simulator -- --key-file config/session.key
# In a second terminal:
dotnet run --project src/Windows/Host -- --config config/local.json
```

Both listeners bind only to loopback. Keep the generated session key private. The local API is at `http://127.0.0.1:23452/v1`; it requires `Authorization: Bearer <the lowercase hex session key>` and rejects browser Origin requests. See [API examples](docs/LOCAL_API.md).

## Run with the iPhone

An iOS build toolchain and authorized installation method are required. Build instructions are in [BUILD_IOS.md](docs/BUILD_IOS.md). After installation, launch BackThread, tap Pair, provision the host's key, and reopen the app. Select `transport: "usb"` in the host configuration. No Wi-Fi, cloud service, or internet on the iPhone is needed for USB audio and file jobs.

The agent listens on iPhone loopback port 23451. Windows reaches it through Apple Mobile Device Service on localhost port 27015. No extra port forward is required for BackThread. OpenSSH diagnostics use a separately configured loopback forward and strict host-key verification.

The UI package embeds the Agent. A separate manually started Agent package is also built; run only one listener on port 23451. Neither package installs a launchd service or session key. An ad-hoc signature establishes artifact integrity, not device authorization. Device preparation, jailbreak, firmware changes and Activation Lock remain outside this project.

## Guides

- [Architecture](docs/ARCHITECTURE.md), [wire protocol](docs/PROTOCOL.md), [security](docs/SECURITY.md)
- [Windows build and use](docs/BUILD_WINDOWS.md), [iOS build and install](docs/BUILD_IOS.md)
- [Dependencies and research](docs/DEPENDENCIES.md), [performance evidence](docs/PERFORMANCE.md)
- [Progress](docs/PROGRESS.md), [known issues](docs/KNOWN_ISSUES.md), [next steps](docs/NEXT_STEPS.md), [decisions](docs/DECISIONS.md)

Original code is MIT licensed. Third-party notices and platform SDK conditions remain separate. No TetherSender source was copied.
