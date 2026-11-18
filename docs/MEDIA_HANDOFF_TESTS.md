# Hybrid Media Handoff & Soak Test Verification Ledger

This report documents the automated verification suite across `.NET 8`, `C11`, Python toolchain checks, and ARM64 iOS 12 cross-builds, while preserving strict separation from untested physical iPhone 6 / Windows desktop execution.

## Automated Test Suites Verified

### 1. `.NET 8` Unit, Integration & Simulator Soak Runner (`36/36 PASS`)
Executed via `dotnet run --project tests/BackThread.Tests -c Release` (targeting Windows 10 contracts on Linux via `-p:EnableWindowsTargeting=true`, running live loopback TCP sockets + `AgentServer` simulator):

- **Hybrid Media Router & Ownership (`HybridMediaTests.cs`)**:
  - `media.router-confirmed-handoff-pauses-windows-once-and-rejects-stale`: Confirms Windows stays playing during `TransferPending`, pauses **only once** upon matching `confirmed` generation, ignores stale old-generation updates, and allows seamless `ReturnToWindows`.
  - `media.router-transfer-failure-keeps-windows-playing`: Confirms failed or unsupported handoff keeps Windows playing untouched.
  - `media.router-focus-and-usb-disconnect-sync`: Confirms `DirectInternet` playback survives simulated USB disconnect while `media.sync` reconciles active generation/position on reconnect, and `AudioFocusPolicy` propagates across session updates.
  - `media.host-receives-device-focus-and-return`: Verifies iPhone-initiated `media.focus.set` & `media.return_to_windows` updates Windows `MediaRouter` ownership end to end.
  - `media.buffered-usb-mode-b-chunks-and-validation`: Validates local `.mp4` Mode B streaming (`media.buffered.start`, ordered 256 KiB Channel 6 chunks, offset mismatch rejection, `media.buffered.eof`).
  - `media.prober-and-youtube-oembed-adapter`: Verifies HTTPS prober hygiene (HTML page rejection, SSRF IP literal rejection), YouTube canonical URL parser (`watch`, `youtu.be`, `shorts`, `live`, timestamps), oEmbed JSON decoding, and `youtube-nocookie` opt-in capability gates.
  - `spotify.pkce-refresh-commands-and-error-mapping`: Verifies PKCE S256 challenge/verifier validation, CSRF state checks, automatic `401` token refresh, Connect device/playlist parsing, and `403`/`404`/`429` error mapping.
  - `events.rich-priorities-and-privacy-masking`: Verifies Priority 0-3 preservation, privacy masking stripping sensitive icons/titles/bodies, source elevation (`HighNotificationSources` / `CriticalNotificationSources`), and blocked source suppression.
  - `host.local-api-media-spotify-and-native-messaging`: Verifies Kestrel loopback bearer authentication, strict browser `Origin` rejection on `/v1/media/*` and `/v1/spotify/*`, SSE `/v1/media/events`, PKCE callback completion, `NativeMessagingHost` stdio framing round-trip, and `HostConfig.Load("config/example.json")` validation.
- **Core Protocol, Security, Transport & Soak (`Program.cs` & `IntegrationTests.cs`)**:
  - Covers framing rejection, HMAC wrong-key/replay rejection, bounded queues, 4 MiB resumable uploads + SHA-256 verification, corrupt artifact range recovery, acceptance deadline/tail exclusion, audio-error isolation, and `soak.concurrent-audio-worker`.

### 2. Phase 9 Live PCM Zero-Loss Soak Comparison (`hybrid-soak-30s.json`)

| Run | Duration | Concurrent 4-MiB Jobs | Packets / Bytes | Peak Buffer (limit 19,200 B) | Sender Drops | Receiver Gaps | Overflow / Dropped B | Silence B | Gate Result |
|---|---|---|---|---|---|---|---|---|---|
| Historical baseline (`repairs-soak-final-metrics.json`) | 120.33s | 98 | 11,901 / 22.85 MB | 19,200 B | 118 | 20 | 5,760 B | 243,840 B | **FAIL** |
| Phase 9 15s Baseline (this session, post-Phase 8) | 15.17s | 48 | — | 9,600 B | 0 | 0 | 0 B | 0 B | **PASS (Sim)** |
| **Phase 9 25s Full Suite (`task-678`)** | **25.19s** | **82** | **2,520 / 4.84 MB** | **9,600 B** | **0** | **0** | **0 B** | **0 B** | **PASS (Sim)** |
| **Phase 9 30s Soak ([`hybrid-soak-30s.json`](reports/hybrid-soak-30s.json))** | **30.15s** | **98** | **3,014 / 5.79 MB** | **9,600 B** | **0** | **0** | **0 B** | **0 B** | **PASS (Sim)** |

Phase 9 achieved identical worker upload throughput (`98` 4-MiB SHA-256 jobs) with zero drops, zero gaps, zero overflow, and zero silence while keeping peak receiver buffer occupancy at 50% capacity (`9,600 B`), due to:
1. Decoupling synchronous Channel 6 disk flushes into a dedicated bounded background queue (`AgentServer.cs`).
2. Removing per-10ms `CancellationTokenSource.CreateLinkedTokenSource` allocations (`Connection.cs`).
3. Enlarging Windows capture ring to 250ms and removing `Ring.Clear()` on 50ms scheduler hiccups (`LoopbackBridge.cs`).
4. Adding `_underrunStreak` re-priming recovery in `BTAudioReceiver.m`.

### 3. Native C11, Python Tooling & iOS ARM64 Cross-Build
- **C11 Native Wire & PCM Ring (`core_test.c`)**: Compiled with `-std=c11 -Wall -Wextra -Werror` and passed under BOTH `zig cc` (Clang 18.1.6) and `gcc`.
- **iOS ARM64 / iOS 12.0 Cross-Build (`build-ios.py`)**: 19 Objective-C / C translation units compiled cleanly with `-Werror -Werror=unguarded-availability-new` and linked with LLD 23.1.1 against iPhoneOS 12.4 SDK (`BackThread`, `BackThread.Agent`, `libBackThread.Agent.a`, `.deb` packages, and `BackThread.ipa`).
- **Clang Static Analyzer (`analyze-ios.py`)**: `22/22` translation units analyzed (`clean=True`).
- **Python Packaging & Device Tooling Unittests**: `22/22 OK` (`ios_build_test.py`, `ios_analyzer_test.py`, `ios_device_tool_test.py`).

## Explicitly Pending Physical Hardware Acceptance

The following items require physical Windows 10 x64 hardware + connected iPhone 6 (iOS 12.5.8) and remain **not tested** in the Linux VM environment:
- Acoustic listening test of Phase 9 `LoopbackBridge` / `Connection` / `BTAudioReceiver` fixes over real Apple usbmux USB.
- Physical iPhone 6 `UIKit` visuals, touch interactions, A8 thermal/memory soak with live `AVPlayer` HLS & Mode B video playback, and lightning cable physical unplug/replug `media.sync` recovery.
- Live Windows 10 `BackThread.Tray.exe` WinForms GUI dialogs, GSMTC session control against real Windows apps, interactive Spotify PKCE browser login, Chromium MV3 extension Native Messaging on Windows Registry, and optional signed MSIX `UserNotificationListener` permission prompt.
