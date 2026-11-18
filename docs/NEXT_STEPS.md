# Next Steps — Physical Windows 10 & iPhone 6 Acceptance Checklist

All software engineering milestones, unit/integration suites, simulator zero-loss soak, C11 tests, static analysis, ARM64 cross-builds (`BackThread.ipa`), self-contained `win-x64` release bundle, and documentation are updated in the repository. Note that `6b986ce` / `6c2fb35` recorded a continuous 60-second WASAPI listening run on hardware (`Có tiếng, nghe liền mạch`; `17,416` unlocalized capture zero-fill bytes) prior to the full `0.2.0` + post-UI2 feature continuation. The remaining steps require physical Windows 10 desktop hardware and a Lightning-connected iPhone 6 (iOS 12.5.8).

1. **Verify Device Readiness & Upgrade Only BackThread App**:
   - Connect iPhone 6 over Lightning USB and confirm Apple Mobile Device Service sees the paired UDID (`python scripts/diagnose-hardware.py`).
   - Install/upgrade `artifacts/ios-build/BackThread.ipa` via `python scripts/install-ios.py --component ui --install` without modifying jailbreak, AMFI, or system components.
   - Verify `library/BackThread/session.key` persistence or provision the host key via **Setup -> Pair** inside the app.

2. **Re-Test Phase 9 Live PCM Audio Through Physical Speaker**:
   - Run `BackThread.DeviceTests.exe --key-file config/session.key --fixture --seconds 3` and verify clean audibility.
   - Play ordinary Windows WASAPI media via `BackThread.Tray.exe` and verify whether Phase 9 (`AgentServer` / `Connection` / `LoopbackBridge` / `BTAudioReceiver` re-priming) eliminates physical stutter (`repairs-physical-wasapi90.json` comparison) under normal CPU load.
   - Test route switches (Speaker <-> Headphones), screen off/lock, and incoming phone interruptions (`BTAudioSessionCoordinator`).

3. **Physical External PiP Acceptance (Mode A, Mode B & Ambient)**:
   - Submit an HTTPS `.mp4`/`.m3u8` URL via Tray *Send Media to iPhone* (or Chromium extension): confirm `AVPlayer` enters fullscreen PiP (`BTVideoSurfaceView`), Windows GSMTC pauses **only after** `confirmed`, and video fills edge-to-edge smoothly on A8.
   - Test **Ambient Mode**, **720p / 480p caps**, **Keep Awake**, and **Audio Focus** (`DuckVideo`, `KeepVideoMuteAudio`, `LivePcmOnly`).
   - Physically unplug the Lightning cable mid-playback during Mode A: confirm Wi-Fi video continues unbroken (`Wi-Fi Independent · USB Away`); replug USB and verify `media.sync` restores host sync cleanly.
   - Send a local Windows `.mp4` file via Mode B buffered USB and verify chunked playback & memory stability.

4. **Windows Integrations, Spotify & Notifications Verification**:
   - Configure `spotifyClientId` in `config/local.json`, complete browser PKCE sign-in via Tray, and verify Connect transport + DPAPI token survival across Host restart.
   - Test Chromium MV3 unpacked extension + `install-native-host.ps1` Native Messaging flow.
   - Test Priority 1–3 notifications over playing PiP video (`BTNotificationOverlayView`) to confirm fullscreen red alerts dismiss cleanly back to playing video without tearing down `AVPlayer`, and optionally sign/register `packaging/msix/Package.appxmanifest` on Windows 10 for native `UserNotificationListener` consent.

5. **30-Minute Mixed Endurance Soak**:
   - Run simultaneous External PiP/Live PCM, file transfers/worker hashes, and periodic notifications on physical iPhone 6 to collect extended RSS, thermal, and battery headroom baselines.
