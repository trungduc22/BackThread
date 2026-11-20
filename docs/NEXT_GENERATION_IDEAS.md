# BackThread Next Generation — Independent Architecture Audit & Future Ideas

Independent review of `BackThread` (`master` + Milestones C–H) and its ecosystem integration points (`NativeShellKit`, `HiberDesk`, `GhostCode`, `ed-local-ai`, `PhoneBox`). Confirmed defects are separated from speculative future enhancements.

---

## Confirmed Codebase Defects & Immediate Hardening (P0)

### 1. [P0] Encrypt Per-Device Keys at Rest in `DeviceRegistry` via DPAPI
- **Problem:** `DeviceRegistry` persists `PairedDeviceRecord.DeviceKeyHex` into `%LOCALAPPDATA%\BackThread\devices.json` as plaintext JSON. While `ToPublic()` prevents leaking keys over HTTP/IPC and file ACLs isolate the user profile, local disk reads by another process under the same account could extract paired Wi-Fi keys.
- **User Benefit:** Protects paired iPhone/legacy-web keys at rest using Windows `CurrentUser` DPAPI (matching `SessionKeyGuard` and `NativeShellKit.Security.DpapiSecretStore`).
- **Affected Components:** `src/Windows/Transport/DeviceRegistry.cs`, `src/Windows/Daemon/SessionKeyGuard.cs`.
- **Difficulty:** Low (1–2 hours).
- **Performance Impact:** Negligible (encrypt/decrypt only on pair, rotate, revoke, and daemon startup).
- **Security Considerations:** Use explicit entropy (`BackThread.DeviceRegistry.v1`) and fall back gracefully on Linux CI tests where DPAPI is unavailable.
- **Dependencies:** `System.Security.Cryptography.ProtectedData` (Windows-only guard).
- **Evidence:** `src/Windows/Transport/DeviceRegistry.cs` (`PersistUnsafe` serializes `PairedDeviceRecord` array directly to `persistPath`).

### 2. [P0] Replace Obsolete `ShellIndicatorState` Usages in `BackThread.App`
- **Problem:** Windows CI (`windows-desktop`) emits repeated compiler warnings (`CS0618`) in `src/Windows/App/Pages/OverviewPage.xaml.cs` lines 34–36 and 57 because it still references obsolete `ShellIndicatorState` from `NativeShellKit 0.1.0-alpha.1` instead of `ShellStatusKind` + `StatusBadge` introduced in `0.2.0-alpha.4`.
- **User Benefit:** Prevents breakage when `NativeShellKit` reaches `1.0.0` and removes obsolete 0.1.x symbols, and brings BackThread's WinUI dashboard onto Fluent 2 `StatusBadge`.
- **Affected Components:** `src/Windows/App/Pages/OverviewPage.xaml`, `src/Windows/App/Pages/OverviewPage.xaml.cs`.
- **Difficulty:** Low (<1 hour).
- **Performance Impact:** Neutral.
- **Security Considerations:** None.
- **Dependencies:** `NativeShellKit.DesignSystem` `0.2.0-alpha.4` (already pinned in `Directory.Packages.props`).
- **Evidence:** GitHub Actions `windows-desktop` build annotations on `src/Windows/App/Pages/OverviewPage.xaml.cs#L34-57`.

### 3. [P0] Port `HostInstanceLease` Single-Host Guard from `integration/hiberdesk-bridge`
- **Problem:** `BackThread.Daemon` uses a Win32 mutex (`Local\BackThread_Daemon_v1`), but running standalone `BackThread.Host.exe` simultaneously with `BackThread.Daemon.exe` can still contend for the same USB device or API port `23452`.
- **User Benefit:** Eliminates port-binding crashes and USB contention when launching via HiberDesk vs standalone CLI/Daemon.
- **Affected Components:** `src/Windows/Host/HostRuntime.cs`, `src/Windows/Daemon/Program.cs`, `integration/hiberdesk-bridge:src/Windows/Host/HostInstanceLease.cs`.
- **Difficulty:** Low (2 hours).
- **Performance Impact:** Zero runtime overhead (single file lock on startup/shutdown).
- **Security Considerations:** Store lock files strictly under `%LOCALAPPDATA%\BackThread\runtime\`.
- **Dependencies:** None.
- **Evidence:** `integration/hiberdesk-bridge` (`5a29fcf`) contains `HostInstanceLease.cs`, which was preserved during Milestone B cleanup pending selective port.

---

## High-Value Improvements (P1)

### 4. [P1] Multi-Device Concurrent Connection Multiplexing (Read-Only + Input + Audio Routing)
- **Problem:** `HostRuntime.RunAsync` currently runs a single active `Connection` loop (`activeSessionCts` / `activeDeviceId`), meaning a second paired iPad or old iPhone cannot act as a Touchpad/Drawing Pad while the primary iPhone is streaming audio.
- **User Benefit:** Lets a user turn an old iPad (iOS 9–12) into a dedicated desk Touchpad/Drawing Pad while their primary iPhone handles notifications and media handoff simultaneously.
- **Affected Components:** `src/Windows/Host/HostRuntime.cs`, `src/Windows/Transport/LanTlsServer.cs`, `src/Windows/Host/RemoteInputEngine.cs`.
- **Difficulty:** Medium (1–2 days).
- **Performance Impact:** Low (~2–4 MB RAM per additional idle TLS session; bounded max 4 concurrent devices).
- **Security Considerations:** Enforce per-device `AllowTouchpad`/`AllowDrawingPad` permissions independently per session and ensure single-owner audio/media routing lock.
- **Dependencies:** Existing `DeviceRegistry` per-device keys (completed in Milestone C).
- **Evidence:** `src/Windows/Host/HostRuntime.cs` lines 470–660 bind a single `connection` field per outer `while` iteration.

### 5. [P1] Native iOS Touchpad & Drawing Pad UIKit View (`BTTouchpadView`)
- **Problem:** Touchpad and Drawing Pad are currently exposed over both BTH1 (`input.touchpad` / `input.draw`) and the TLS Legacy Web Client (`LegacyWebServer.cs`), while the native iOS UIKit app (`BTViewController.m`) does not yet include a dedicated Touchpad/Draw tab.
- **User Benefit:** Gives stock iOS 12+ and iOS 15+ devices sub-5ms native `UITouch` / `coalescedTouchesForTouch:` input without opening Safari, plus hardware-backed `UITouch.force` (3D Touch / Apple Pencil) where physically exposed.
- **Affected Components:** `src/iOS/UI/BTViewController.m`, new `src/iOS/UI/BTTouchpadView.h/.m`, `src/iOS/Agent/BTAgent.m`.
- **Difficulty:** Medium (1–2 days).
- **Performance Impact:** Lower battery and CPU than WebView/Safari on iPhone 6 (A8 / 1 GB RAM).
- **Security Considerations:** Only transmit input when PC explicitly advertises `allowTouchpad`/`allowDrawingPad` in `hello`; include prominent Emergency Stop button.
- **Dependencies:** Existing `RemoteInputEngine` and BTH1 `input.touchpad`/`input.draw` handlers (completed in Milestone G).
- **Evidence:** `src/Windows/Host/HostRuntime.cs` handles `input.touchpad` / `input.draw`, while `src/iOS/UI/` currently hosts Now Playing, Idle Hub, Quick Actions, Worker, and Settings views.

### 6. [P1] WinUI 3 Dashboard Controls for Remote Input, Pairing QR & Per-Device Permissions
- **Problem:** `BackThread.Daemon` now exposes `input.status`, `input.emergency.stop`, `lan.devices.rotate`, `lan.devices.disconnect`, and `lan.devices.permissions` over authenticated Named Pipe IPC, but `BackThread.App` does not yet render toggle switches or a one-click Emergency Stop banner for them.
- **User Benefit:** Allows users to authorize/revoke Touchpad & Drawing Pad per device, rotate keys, and select target monitors from the native Fluent 2 WinUI dashboard without editing JSON.
- **Affected Components:** `src/Windows/App/Pages/`, `src/Windows/App/Services/DaemonClient.cs`.
- **Difficulty:** Medium (1 day).
- **Performance Impact:** Zero daemon overhead when dashboard is closed.
- **Security Considerations:** Require explicit confirmation dialog (`IDialogService` from `NativeShellKit.WinUI`) before enabling Touchpad or Drawing Pad for a device.
- **Dependencies:** `NativeShellKit.WinUI` + `NativeShellKit.DesignSystem` `0.2.0-alpha.4`.
- **Evidence:** `src/Windows/Daemon/Contract/DaemonContract.cs` and `DaemonIpcHost.cs` expose all 5 operations ready for `BackThread.App`.

### 7. [P1] Migrate `BackThread.Host.Core` / `BackThread.Tests` Job Journal Isolation from `codex/native-foundation`
- **Problem:** `codex/native-foundation` (`4710def`, preserved during Milestone B) contains verified fixes isolating native job journal I/O from the real-time PCM audio path.
- **User Benefit:** Prevents storage I/O jitter from ever causing PCM audio underruns during simultaneous file/worker offloading on low-end Windows laptops.
- **Affected Components:** `src/Windows/Worker/`, `src/Simulator/`, `tests/BackThread.Tests/`.
- **Difficulty:** Medium (0.5–1 day).
- **Performance Impact:** Measurable reduction in tail PCM audio pacing latency under disk load.
- **Security Considerations:** Preserve existing bounded file paths and SHA-256 verification.
- **Dependencies:** Selective cherry-pick/port from preserved branch `codex/native-foundation` (`4710def`).
- **Evidence:** `git log origin/master..origin/codex/native-foundation` (`4710def`, `c6435ad`).

---

## Optional Enhancements (P2)

### 8. [P2] Windows Ink Pen Injection (`InjectSyntheticPointerInput`) for Real Stylus Hardware
- **Problem:** `RemoteInputEngine` currently accepts honest `hasRealPressure`, `pressure`, `tiltX`, `tiltY`, and `penButtons` metadata, but `Win32RemoteInputSink` injects `INPUT_MOUSE` events (no pressure/tilt curve in drawing apps like Krita/Photoshop).
- **User Benefit:** Enables true pressure-sensitive drawing from Apple Pencil iPads or S-Pen Android tablets when the hardware exposes genuine pressure values.
- **Affected Components:** `src/Windows/Host/RemoteInputEngine.cs` (`Win32RemoteInputSink`).
- **Difficulty:** Medium-High (2 days; requires Windows 10 1809+ `CreateSyntheticPointerDevice` + `InjectSyntheticPointerInput` P/Invoke and strict fallback to `INPUT_MOUSE` when `hasRealPressure == false`).
- **Performance Impact:** Minimal (<1% CPU at 120 Hz).
- **Security Considerations:** Must NEVER fabricate pressure values on non-stylus touchscreens; separate permission gate `AllowStylusPen` (default `false`).
- **Dependencies:** Windows 10 1809+ `user32.dll` synthetic pointer APIs.
- **Evidence:** `DrawingPadEvent` in `src/Windows/Host/RemoteInputEngine.cs` already defines `HasRealPressure`, `Pressure`, `TiltX`, `TiltY`, `PenButtons`.

### 9. [P2] Binary Compact WebSocket Frame Format for Touchpad / Drawing Pad
- **Problem:** `LegacyWebServer` WebSocket input currently sends compact JSON text frames (~85 bytes/event). On 120 Hz screens over noisy 2.4 GHz Wi-Fi, JSON serialization adds avoidable overhead.
- **User Benefit:** Cuts input payload size by ~5× (16-byte fixed binary struct) and eliminates per-event GC allocations on old devices and low-end PCs.
- **Affected Components:** `src/Windows/Host/LegacyWebServer.cs`, `AppJsEs5` (feature-detected `ArrayBuffer` / `DataView` fallback to JSON).
- **Difficulty:** Low-Medium (0.5 day).
- **Performance Impact:** Reduces Wi-Fi airtime and GC pressure during rapid drawing strokes.
- **Security Considerations:** Enforce fixed frame length and authentication state before parsing.
- **Dependencies:** Feature detection for `ArrayBuffer` (iOS 8+ supported).
- **Evidence:** `HandleWebSocketAsync` in `src/Windows/Host/LegacyWebServer.cs` parses `WebSocketMessageType.Text` via `JsonDocument.Parse`.

### 10. [P2] Ecosystem-Wide Instant SDK Dispatch Secret (`ECOSYSTEM_DISPATCH_TOKEN`) + `PackageValidationBaselineVersion`
- **Problem:** All 4 consumer repos now have daily Dependabot + `repository_dispatch` (`nativeshellkit-released`) workflows, but instant push-on-tag dispatch waits for `ECOSYSTEM_DISPATCH_TOKEN` secret setup, and `NativeShellKit` binary baseline validation (`0.2.0-alpha.4`) should activate on `0.2.0-alpha.5+`.
- **User Benefit:** Shrinks SDK patch adoption latency from up to 24h to <5 minutes with automated binary-compatibility enforcement.
- **Affected Components:** `NativeShellKit/src/Directory.Build.props`, GitHub repository secrets.
- **Difficulty:** Low (15 minutes).
- **Performance Impact:** None.
- **Security Considerations:** Scope PAT strictly to `contents: write` on the 4 consumer repositories only.
- **Dependencies:** Maintainer secret configuration in GitHub Settings.
- **Evidence:** `NativeShellKit/.github/workflows/publish-packages.yml` lines 105–125 and `NativeShellKit/src/Directory.Build.props`.

---

## Experimental Ideas (P3)

### 11. [P3] Dedicated Desk Clock / Ambient Pomodoro & Quick Macro Deck Mode for Legacy Tablets
- **Problem:** Old iPads/iPhones (iOS 8–12) placed under a monitor spend >80% of the day idle between active touchpad or media sessions.
- **User Benefit:** Adds an ultra-low-power OLED-safe ambient clock + PC CPU/GPU temperature glance + allowlisted BackThread Action triggers (e.g., mic mute, play/pause) inside the existing `<15 KB` ES5 web companion.
- **Affected Components:** `src/Windows/Host/LegacyWebServer.cs` (`IndexHtml`, `AppJsEs5`).
- **Difficulty:** Low (0.5 day).
- **Performance Impact:** Near-zero (5-second polling interval, zero animations).
- **Security Considerations:** Actions invoked strictly by PC-defined `AuthorizedAction.Id` allowlist (never arbitrary commands).
- **Dependencies:** Existing `HostConfig.Actions` allowlist.
- **Evidence:** `HostConfig.Actions` in `src/Windows/Host/HostConfig.cs` already defines safe ID-based actions.
