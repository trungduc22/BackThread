# BackThread

BackThread transforms a docked iPhone (tested target: **iPhone 6, ARM64, iOS 12.5.8**) over a single Lightning USB cable into an external Windows 10 companion surface featuring:

- **External Picture-in-Picture (PiP)**: Hardware `AVPlayer` independent playback via iPhone Wi-Fi (`Mode A · DirectInternet`) or chunked USB streaming (`Mode B · BufferedUsb`) with a persistent single `AVPlayerLayer` across preview, fullscreen focus, and dark Ambient modes.
- **Hybrid Media Router & Official Integrations**: Monotonic generation fencing, `CONFIRMED`-only Windows auto-pause, `AudioFocusPolicy` arbitration, USB disconnect survival (`media.sync` reconnect reconciliation), official **Spotify Web API OAuth PKCE** (`DPAPI` local tokens, Mode D remote control), and compliant **YouTube oEmbed** metadata / optional isolated `youtube-nocookie` embed fallback.
- **Low-Jitter USB PCM Audio & 3-Mode Output Routing**: Real-time 48 kHz stereo PCM16 streaming (`LoopbackBridge` WASAPI capture -> bounded `BTH1` channel 2 -> 19,200-byte native C ring -> `AudioQueue` with post-underrun re-priming), **Only PC / Only Phone / Both** routing with automatic Windows render mute save/restore (`WindowsEndpointAudio`), capture signal monitoring, and an **inline labeled volume slider** (`PC Vol` / `App Gain` / `AVPlayer`) right on the playback screens.
- **Live Clock, Telemetry Strip & Non-Destructive Top-Bar Notifications**: Always-visible `HH:mm:ss` monospace clock, iPhone battery/Wi-Fi (`UIDevice` + `SystemConfiguration`), Windows CPU/RAM/Battery/Wi-Fi & strict Bluetooth connection state (`Aep.IsConnected`), Reduce-Motion-aware equalizer animations, and **top-bar-only** Priority 1–3 notifications (`"spotlight"` & `"beacon"` styles) layered above External PiP video without interrupting `AVPlayer`, plus MSIX toast collection support (`Build-Msix.ps1`), allowlisted Quick Actions, Chromium MV3 Native Messaging extension, and bounded A8 jobs.

> **Current Verification Status (`0.2.0` Build 6, Continuing from `0.1.2` `307e167`):**
> All Windows host libraries, Tray UI, Simulator, Chromium extension, MSIX helper (`packaging/msix/Build-Msix.ps1`), **`42/42` automated `.NET` socket/hybrid-media/audio-mode/telemetry/soak tests** (`20.07s` concurrent 63-job zero-loss simulator soak), `C11` shared wire/ring tests (`zig cc` & `gcc` ASan/UBSan), **`23/23` Python packaging/analyzer tests**, **`23/23` Clang static-analyzer units (`clean=True`)**, `20`-unit ARM64 iOS 12 cross-builds (`BackThread.ipa` / `.deb`, `CFBundleVersion=6`), and `650`-file `win-x64` release bundle pass in the Linux engineering environment.
> Historically on physical iPhone 6 hardware (`0.1.2` and `6b986ce`), authenticated USB, embedded Agent execution, file jobs, audible speaker fixture, and continuous 60-second WASAPI audio were verified prior to cable unplug. **Newest `0.2.0` Build 6 iOS UI visuals, MMDevice mute/scalar routing, `UserNotificationListener` MSIX consent, and Wi-Fi `AVPlayer` playback remain pending physical iPhone 6 and Windows 10 reconnection acceptance.** Read [feature status](docs/FEATURE_STATUS.md), [hardware evidence](docs/HARDWARE_TEST_RESULTS.md), [media handoff tests](docs/MEDIA_HANDOFF_TESTS.md), and [next steps](docs/NEXT_STEPS.md).

## Build and verify

Use .NET 8 SDK (`8.0.428`+), with `-p:EnableWindowsTargeting=true` when cross-building from Linux, plus Zig `0.13.0` / LLD `23.1.x` / iPhoneOS 12.4 SDK / `rcodesign` for iOS ARM64 binaries:

```powershell
dotnet build BackThread.sln -c Release
./scripts/test.ps1
./scripts/test.ps1 -SoakSeconds 30
python scripts/publish-windows-release.py   # or ./scripts/build.ps1 -Publish on Windows
python scripts/build-ios.py --linker <lld> --zig <zig> --sdk <iPhoneOS12.4.sdk> --codesign <rcodesign>
python scripts/analyze-ios.py --zig <zig> --sdk <iPhoneOS12.4.sdk>
python scripts/install-ios.py --component ui
```

The `.NET` test runner is an executable with nonzero exit on failure (`dotnet run --project tests/BackThread.Tests -c Release`, not `dotnet test`).

## Quick Start (Windows + iPhone or Simulator)

```powershell
# 1. Provision session key & default configuration (keep config/session.key private)
dotnet run --project src/Windows/Host -- --init-key config/session.key
Copy-Item config/example.json config/local.json

# 2a. Simulator workflow (set transport="localhost" in config/local.json)
dotnet run --project src/Simulator -- --key-file config/session.key
dotnet run --project src/Windows/Host -- --config config/local.json

# 2b. Physical USB workflow (transport="usb", deviceHasInternet=true; Apple Mobile Device Service running)
# Provision config/session.key inside BackThread iOS Setup -> Pair, then run:
dotnet run --project src/Windows/Tray -- (Resolve-Path config/local.json)
```

Loopback HTTP API listens strictly at `http://127.0.0.1:23452/v1`, requires `Authorization: Bearer <64-hex key>`, and rejects browser `Origin` requests. See [LOCAL_API.md](docs/LOCAL_API.md).

## Documentation Index

- **Architecture & Media Engine**: [ARCHITECTURE.md](docs/ARCHITECTURE.md), [HYBRID_MEDIA_ARCHITECTURE.md](docs/HYBRID_MEDIA_ARCHITECTURE.md), [EXTERNAL_PIP.md](docs/EXTERNAL_PIP.md), [MEDIA_PROVIDER_COMPATIBILITY.md](docs/MEDIA_PROVIDER_COMPATIBILITY.md)
- **Integrations, Notifications & UI**: [SPOTIFY_INTEGRATION.md](docs/SPOTIFY_INTEGRATION.md), [YOUTUBE_INTEGRATION.md](docs/YOUTUBE_INTEGRATION.md), [NOTIFICATION_ACCEPTANCE.md](docs/NOTIFICATION_ACCEPTANCE.md), [UI_DESIGN.md](docs/UI_DESIGN.md)
- **Protocol, API & Security**: [PROTOCOL.md](docs/PROTOCOL.md), [LOCAL_API.md](docs/LOCAL_API.md), [SECURITY.md](docs/SECURITY.md), [DEPENDENCIES.md](docs/DEPENDENCIES.md)
- **Builds, Tests & Status**: [BUILD_WINDOWS.md](docs/BUILD_WINDOWS.md), [BUILD_IOS.md](docs/BUILD_IOS.md), [FEATURE_STATUS.md](docs/FEATURE_STATUS.md), [MEDIA_HANDOFF_TESTS.md](docs/MEDIA_HANDOFF_TESTS.md), [PERFORMANCE.md](docs/PERFORMANCE.md), [PROGRESS.md](docs/PROGRESS.md), [KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md), [NEXT_STEPS.md](docs/NEXT_STEPS.md)

Original source is MIT licensed ([LICENSE](LICENSE)); third-party notices remain separate ([THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)). No GPL `youTFast`, stream extractors, DRM bypasses, or `TetherSender` source are included.
