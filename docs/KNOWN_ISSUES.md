# Known issues and limits — 0.1.2

## Current UI 2.0 limitations — 2026-11-18

- Latest top-bar-only notifications, system/usage strip, three-way audio routing and inline volume request is not implemented. Existing critical fullscreen behavior must change.
- Real Wi-Fi handover timed out after successful USB enrollment. Listener was revoked; physical TLS is unverified.
- Portable Windows Host lacks package identity/userNotificationListener capability; regular Windows notification collection is unavailable. No notification permission was silently granted.
- Paired installer currently returns InvalidService. User installed build5 independently; do not repair activation/system/jailbreak components.
- Native UI inspection passed, but app grid is empty, phone-to-PC tap launch, touch gestures, direct video, font scaling, long-duration memory and interruption tests remain open.
- Audit follow-up: `BTAgent.setNativePlaybackActive` changes the flag and stops the receiver in separate critical sections; `audio.start` checks ownership before calling receiver start. Review/serialize the complete ownership/start operation to prevent a narrow concurrent handoff race. This is a source-review concern, not a reproduced crash; no fix or acceptance claim yet.
- UI polish seen in real snapshot: IP placeholder has poor dark-mode contrast; page indicators can overlap the last partially visible Settings row; check new status-strip space before expanding controls.
- Ten-second command deadline reports an unknown outcome; do not automatically retry a launch that might already have executed.

## Historical issues follow

1. **Audio stutters.** A new short fixture and ordinary Windows WASAPI playback were heard on the physical iPhone. The corrected 90-second profile still shows significant sender/receiver loss and the user reported stutter. Latest pooled single-write Windows transport has not been physically tested after the user unplugged USB. Earlier two silent tests remain recorded; no universal root cause is established.
2. **Native repairs require real runtime validation.** Audio errors now discard queued PCM and preserve the USB session; reset checks playback intent under the lifecycle lock; failed volume requests return errors. These compile/analyze; physical failure injection, route/interruption/reset and stop/start behavior remain unverified.
3. **No system repair.** The user has unplugged USB; device work is waiting for reconnection. Missing frameworks/services in the user's prepared system are recorded as blockers. No firmware, activation, trust cache, AMFI, jailbreak component, launchd or system configuration repair is authorized.
4. **Audio measurements have limits.** Historical 60-second stats include shutdown tail. New runner uses a common pacing deadline and timestamps, excludes late samples, and reports zero-loss observations separately from functional passes. No acoustic latency or long-duration quality certification exists.
5. **Background lifetime is limited.** UIKit may suspend without active audio; termination interrupts jobs. Standalone Agent is packaged but was denied execution. No persistent service or UI/daemon IPC is installed.
6. **Storage/worker coverage is incomplete.** Basic one-MiB upload/SHA/range/memory jobs ran physically. Native quota, interrupted transfer, cancellation/restart and low-memory cases need device tests. Archives are opaque; no snapshot/delta/versioned retention or automatic partial cleanup.
7. **Unicode text semantics differ.** Native text counts ASCII whitespace; .NET uses Unicode whitespace. Do not claim cross-platform Unicode parity.
8. **Windows notification collection is partial.** Packaged identity/capability and interactive consent flow are missing. The authenticated opt-in event API works.
9. **Media/UI acceptance is incomplete.** Real Windows metadata/play/pause/seek fixture passed, but actual phone rendering, gestures, artwork, third-party playlists and Quick Actions need interactive verification. Tray compiles/publishes; visual acceptance and custom app icon are pending.
10. **Independent media is partial.** AVPlayer play invocation lacks a result/error handshake and PCM coordination. YouTube independent playback and Windows default-audio-output switching are not implemented.
11. **Trust/confidentiality boundary.** HMAC authenticates the local USB session, but frames are not encrypted or individually MACed. No public-network mode, kernel sound driver or default-output replacement is provided.
12. **Performance evidence is short.** New native RSS had fifteen samples at 10.81–10.87 MiB; older twelve-sample evidence was 11.30–11.55 MiB; no long-term leak, Jetsam, temperature, battery or acoustic test. Simulator results remain simulator results.
13. **Toolchain constraints.** Cross-build requires explicit Zig 0.13.0 + LLD 23.1.5 + supplied real SDK + signer. Theos/Xcode routes remain unexecuted. Ad-hoc integrity does not grant device execution rights. No SDK/tool binaries or private credentials ship.
14. **Release scope.** Repair builds are engineering checkpoints; newest native behavior is not installed/runtime-certified. Full acceptance is still incomplete. Every archive must carry its manifest and validation boundary.

See [current feature matrix](FEATURE_STATUS.md), [hardware evidence](HARDWARE_TEST_RESULTS.md) and [repair review](REVIEW_2026-11-18.md).

15. **Transient portable worker IO error.** One latest portable simulator acceptance run failed its SHA job with “Access to the path is denied”; generated fixture could be read afterward. An isolated rerun of the same binaries passed. The exact stage/cause is not established; no ACL/antivirus/system policy was changed. The failed report is retained as repairs-portable-runner-io-failure.json and the passing isolated run separately.
