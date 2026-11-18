# Next Steps — Physical Windows 10 & iPhone 6 Acceptance Checklist

All software engineering milestones (`0.2.0` Build 6, including all five post-UI2 product changes), `42/42` `.NET` tests + `20.07s` concurrent 63-job zero-loss soak, C11 tests, `23/23` Clang static analysis, `20`-TU ARM64 cross-builds (`BackThread.ipa`), self-contained `win-x64` release bundle (`650` files), and documentation are updated in the repository. Note that `6b986ce` / `6c2fb35` recorded a continuous 60-second WASAPI listening run on hardware (`Có tiếng, nghe liền mạch`; `17,416` unlocalized capture zero-fill bytes) prior to this `0.2.0` Build 6 continuation. The remaining checks require physical Windows 10 desktop hardware and a Lightning-connected iPhone 6 (iOS 12.5.8).

1. **Verify Device Readiness & Upgrade Only BackThread App (`0.2.0` Build 6)**:
   - Connect iPhone 6 over Lightning USB and confirm Apple Mobile Device Service sees the paired UDID (`python scripts/diagnose-hardware.py`).
   - Install/upgrade `artifacts/ios-build/BackThread.ipa` via `python scripts/install-ios.py --component ui --install` without modifying jailbreak, AMFI, or system components.
   - Verify `library/BackThread/session.key` persistence or provision the host key via **Setup -> Pair** inside the app.

2. **Re-Test Phase 9 Live PCM Audio, 3-Mode Output & Inline Volume Slider**:
   - Run `BackThread.DeviceTests.exe --key-file config/session.key --fixture --seconds 3` and verify clean audibility.
   - Verify **Only PC / Only Phone / Both** switching on both iPhone (`BTNowPlayingView` / `BTVideoSurfaceView`) and Windows Tray: confirm `PhoneOnly` mutes PC speakers smoothly, switching back or disconnecting USB **always** restores prior PC mute state, and check whether the audio driver emits `silent-driver-coupled` warnings when speaker-muted.
   - Drag the inline labeled volume slider (`PC Vol`, `App Gain`, `AVPlayer`) and confirm readback percentages stay synchronized without UI snapback.

3. **Live Clock, Telemetry Strip & Playback Animations**:
   - Verify `BTTelemetryStripView` shows exact live `HH:mm:ss` clock, genuine iPhone battery/Wi-Fi, real Windows CPU/RAM/Battery/Wi-Fi, and that Bluetooth badge reports `BT: On (0 conn)` when Windows Bluetooth radio is enabled with zero `Aep.IsConnected` peripherals.
   - Verify `BTEqualizerView` animates smoothly during active audio/video playback and freezes/disables when **Settings -> Accessibility -> Reduce Motion** is turned ON.

4. **Physical External PiP Acceptance & Top-Bar-Only Notifications**:
   - Submit an HTTPS `.mp4`/`.m3u8` URL via Tray *Send Media to iPhone* (or Chromium extension): confirm `AVPlayer` enters fullscreen PiP (`BTVideoSurfaceView`), Windows GSMTC pauses **only after** `confirmed`, and video fills edge-to-edge smoothly on A8.
   - Test Priority 1–3 notifications (including `beacon` and `spotlight` styles) over playing PiP video: confirm **all** alerts appear solely as top bars, touches outside the bar control the video underneath, and `AVPlayer` never pauses or tears down.
   - Run `packaging/msix/Build-Msix.ps1` on Windows 10 if mirroring native Action Center toasts via `UserNotificationListener`, approve interactive consent via Tray or `POST /v1/notifications/request-access`, and confirm `/v1/notifications/diagnostics` reports `hasPackageIdentity: true` + `Allowed`.

5. **Windows Integrations, Spotify & 30-Minute Mixed Endurance Soak**:
   - Configure `spotifyClientId` in `config/local.json`, complete browser PKCE sign-in via Tray, and verify Connect transport + DPAPI token survival across Host restart.
   - Test Chromium MV3 unpacked extension + `install-native-host.ps1` Native Messaging flow.
   - Run simultaneous External PiP/Live PCM, file transfers/worker hashes, and periodic notifications on physical iPhone 6 to collect extended RSS, thermal, and battery headroom baselines.
