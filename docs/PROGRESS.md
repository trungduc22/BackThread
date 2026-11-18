# BackThread Progress — 0.2.0 Continuation

## Prior Verified Hardware Checkpoints (`0.1.2` `307e167` & `6b986ce`, 2026-11-18)

- **Resumed 60s Physical WASAPI Run (`6b986ce` / `6c2fb35`)**: `32/32` functional checks, `6,000` PCM frames heard continuously (`Có tiếng, nghe liền mạch`) with `0` sampled native receiver gaps/overflow/silence/sender drops, RSS `12.48–12.57 MiB`, app gain `0.25`, `17,416` unlocalized capture zero-fill bytes ([`resume-physical-wasapi60.json`](reports/resume-physical-wasapi60.json)).

- Physical iPhone 6 (`ARM64`, iOS 12.5.8) over Apple `usbmux`: authenticated USB, embedded Agent execution, 1 MiB uploads/SHA/range jobs, first audible speaker fixture (`Có nghe tiếng`), and stuttered normal WASAPI media (`repairs-physical-wasapi90.json`, 472 sender drops, 132 gaps, 1,416,960 overflow B, 2,423,040 silence B) were captured prior to user USB unplug.

## Autonomous Engineering Milestones Completed (`0.2.0`)

1. **Linux Cross-Host Engineering Baseline (`dd125a6`)**:
   - Guarded P/Invoke Windows calls behind `OperatingSystem.IsWindows()` so Linux cross-hosts build and run `.NET` simulator suites cleanly, and enabled LLD 23.1.x deterministic iOS ARM64 linking + static analysis.
2. **Windows Hybrid Media Engine & Integrations (`9c96121`)**:
   - Implemented [`MediaRouter`](../src/Windows/Media/MediaRouter.cs) (5 ownership states, monotonic generation fencing, `CONFIRMED`-only Windows auto-pause, `AudioFocusPolicy` arbitration, reconnect `media.sync`).
   - Added [`DirectMediaProvider`](../src/Windows/Media/Handoff.cs), [`MediaCapabilityProber`](../src/Windows/Media/Handoff.cs) (SSRF & HTML protections), [`BufferedMediaSource`](../src/Windows/Media/BufferedMediaSource.cs) (Mode B 256 KiB USB chunks), compliant [`YouTubeProvider`](../src/Windows/Media/YouTubeProvider.cs) (oEmbed + Mode D/PCM + opt-in nocookie embed metadata), and official [`SpotifyProvider`](../src/Windows/Media/SpotifyProvider.cs) (OAuth 2.0 PKCE + DPAPI local storage).
   - Extended [`EventRouter`](../src/Windows/Events/EventRouter.cs) (Priority 0–3, dynamic High/Critical source lists, privacy masking), [`LocalApi`](../src/Windows/Host/LocalApi.cs) (`/v1/media/*`, `/v1/spotify/*`, SSE `/v1/media/events`, `/v1/notifications/*`), [`NativeMessagingHost`](../src/Windows/Host/NativeMessagingHost.cs), [`TrayContext`](../src/Windows/Tray/Program.cs), `extensions/chromium/`, and `packaging/msix/`.
3. **Native iOS 12 External PiP & Landscape-First UIKit (`df95167`)**:
   - Built [`BTDirectMediaPlayer`](../src/iOS/Media/BTDirectMediaPlayer.m) (single persistent `AVPlayerLayer`, `preferredPeakBitRate` quality caps, conditional screen wake lock, Wi-Fi USB-disconnect continuity + `media.sync` reporting).
   - Built [`BTBufferedMediaEngine`](../src/iOS/Media/BTBufferedMediaEngine.m) (`AVAssetResourceLoaderDelegate` with 24 MiB bounded LRU cache & memory-warning purge), [`BTYouTubeEmbedController`](../src/iOS/Media/BTYouTubeEmbedController.m), and [`BTAudioSessionCoordinator`](../src/iOS/AudioReceiver/BTAudioSessionCoordinator.m).
   - Built modular `BTTheme` dark-slate landscape UI (`BTIdleHubView`, `BTNowPlayingView`, `BTVideoSurfaceView`, `BTQuickActionsView`, `BTWorkerStatusView`, `BTSettingsView`, `BTNotificationOverlayView` & `BTHistoryPanelView`) where Priority 2 banners and Priority 3 fullscreen alerts float non-destructively above active video.
4. **Live PCM Stutter Root-Cause Repair (`20b9ebe`)**:
   - Decoupled Channel 6 disk flushes into bounded worker queues (`AgentServer.cs`), removed per-10ms `CancellationTokenSource` allocations (`Connection.cs`), expanded Windows capture ring to 250ms with non-destructive `Stopwatch` pacing (`LoopbackBridge.cs`), and added `_underrunStreak` recovery in `BTAudioReceiver.m`.
   - Passed strict zero-loss `30.15s` concurrent **98-job (`392 MiB`) + 3,014-packet PCM soak** (`0` drops, `0` gaps, `0` overflow, `0` silence, `9,600 B` peak ring buffer) in [`hybrid-soak-30s.json`](reports/hybrid-soak-30s.json).
5. **Release Staging, Rendered Mockups & Documentation Suite**:
   - Updated `config/example.json`, `scripts/build.ps1`, added `scripts/publish-windows-release.py`, staged 649 self-contained `win-x64` files + `checksums.sha256` (zero secrets) and refreshed `BackThread.ipa`.
   - Rendered 6 clean UI mockups & architecture diagrams (`docs/images/`) and authored 8 comprehensive architecture/integration/acceptance docs + updated 11 core guides.
