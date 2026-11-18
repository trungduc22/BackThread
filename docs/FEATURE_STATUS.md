# Feature acceptance matrix — 0.1.2

## Latest verified hardware checkpoint — 2026-11-18

**First audible Windows PCM through the iPhone speaker is now verified.** After the normal own-app Upgrade and foreground reopen, the user saw “Connected by USB”. The new three-second PCM fixture passed 27 physical protocol/audio/worker checks and the user answered “Có nghe tiếng”. Current fixture stats show Speaker, 48 kHz, gain 0.25, system volume 1, both peaks 4096, noErr, successful getters, and zero sampled gaps/overflow/underrun. This does not establish a single root cause for earlier silent attempts: app build, launch state and user-controlled system volume differed.

Normal WASAPI media was then heard through the iPhone, with the user reporting “Có nhưng bị ngắt/giật” after normal Host app gain was restored to 1. **Continuous audio quality is not accepted yet.** The separate 90-second diagnostic run at app gain 0.25/system volume about 0.105 received/rendered nonzero PCM but had 472 sender drops, 132 gap events, 1,416,960 overflow bytes and 2,423,040 active-window silence bytes. Fifteen native RSS samples were 11,337,728–11,395,072 bytes. It passed its functional gates, not a zero-loss/acoustic quality gate.

A normal-Host sample measured 0.046875 CPU seconds over 3.04 wall seconds and 84,193,280 bytes RSS; total Windows CPU was reported 100%, with 3,585 MiB available and no paging in that snapshot. This is evidence of load, not proof of one cause for stutter. Its last observed sender-drop counter was zero; that does not prove native receiver/capture quality.

The user subsequently confirmed physically unplugging the cable. USB disappearance is therefore an observed user disconnect, not an unexplained discovery failure. Hardware work is now waiting for reconnection. Latest Windows changes batch each frame into one pooled socket write and add write-wait telemetry; **their physical effect on stutter has not been tested**. No system/framework/service/jailbreak repair was performed.

Evidence: [human confirmations](reports/repairs-human-listening.json), [physical fixture](reports/repairs-physical-fixture.json), [90-second native profile](reports/repairs-physical-wasapi90.json), [normal Host load](reports/repairs-host-load.json). Earlier failed listening reports remain historical evidence.

Evidence date: 2026-11-18, Asia/Saigon. This table is the current assessment and replaces the earlier blocked-entry assessment. The prepared environment was rechecked, the own app upgraded and authenticated again after the user reopened it. The user has now unplugged USB; physical reconnection remains pending. Changes on a phone are limited to BackThread-owned app/data through existing authorized services. No missing system/jailbreak component is repaired or installed by this project.

Classifications distinguish physical iPhone, Windows, simulator, compiled-only, partial, blocked and not implemented. A code path running does not certify its user-facing result.

| Feature | Classification | Verified result / remaining boundary |
|---|---|---|
| USB enumeration and paired certificate pinning | Verified on physical iPhone + Windows | Existing Apple Mobile Device Service and paired TLS worked; no new pairing initiated |
| Embedded Agent execution | Verified on physical iPhone | Full app served real authenticated protocol and jobs; native entry is no longer blocked in the recorded environment |
| Standalone Agent execution | Blocked | Build/package exists; execution from /var/mobile/BackThread was denied (Killed: 9); no execution policy changed |
| Authentication, framing, heartbeats | Verified on physical iPhone; broader faults in simulator | Wrong key and unsupported framing version rejected; subsequent valid session succeeds |
| New-socket reconnection | Verified on physical iPhone | Authentication succeeds after prior session closes; physical unplug/replug and app restart still pending |
| Automatic Host reconnect | Verified in simulator; native session recovery observed | Real Host reauthenticated after app foreground reopen; complete physical cable unplug/replug cycle still unverified |
| WASAPI capture/conversion | Verified on Windows | Real nonzero 48 kHz stereo PCM16 from render endpoint |
| WASAPI → USB → native PCM buffers | Verified on physical iPhone | Known Windows signal produced input/render peak 446 and 147,848 nonzero rendered samples |
| Shared native C wire/ring | Verified in Windows tests and physical data path | Same C implementation used by native receiver; fault/overflow cases tested on Windows |
| AVAudioSession/AudioQueue start/callbacks | Verified on physical iPhone for recorded build | Running queue, noErr, Speaker, 48 kHz and nonzero rendered buffers; not an acoustic measurement |
| Audible iPhone speaker output | Verified on physical iPhone | New fixture heard; normal WASAPI media also heard with stutter. Earlier two listening failures remain historical |
| Audio quality and latency | Partially implemented; quality gate not met | Current corrected 90-second run shows drops/gaps/overflow/underrun and human stutter. No acoustic latency claim; latest Windows batching repair not physically retested |
| Audio failure isolation, reset and volume error repairs | Compiled but not runtime-verified on iPhone | Queue failure no longer intentionally closes native USB session; Stop/reset decision uses lifecycle lock. Host error handling and measurement deadline regressions tested on Windows |
| Routes, interruptions, screen-off playback | Compiled but not runtime-verified | Must test real device; source review/static analysis are insufficient |
| GSMTC metadata/play/pause/seek | Verified on Windows | Actual Windows fixture and command events; phone touch path unverified |
| Previous/next/artwork and native UIKit controls | Compiled but not runtime-verified end to end | Capability-aware source exists; visual, playlist and phone interaction tests pending |
| Generic events/dedup | Verified on physical iPhone reception; simulator privacy/expiry/flood tests | Accepted event and duplicate rejection proved; banner/history appearance unverified |
| Native fullscreen/banner/history UI | Compiled but not runtime-verified visually | UIKit app executed; actual pixels/gestures not confirmed while phone screen was black |
| Windows official notification collector | Partially implemented | Collector source exists; packaged permission/interactive consent flow missing; authenticated opt-in event API works |
| Quick Action registry/allowlist | Verified in simulator | Native UI exists; physical action invocation unverified |
| Windows microphone mute / configured app launch | Compiled but not runtime-verified | Authorized implementations; no acceptance state change performed |
| Switch Windows default audio output | Not implemented | No virtual audio driver or supported default-endpoint switch supplied |
| Upload, whole-file SHA-256, range retrieval, memory job | Verified on physical iPhone for basic flow | One-MiB upload/hash/exact-range worked alongside PCM; quota/cancel/resume/restart native coverage incomplete |
| Text, metadata, verify and worker recovery | Verified in simulator; native compiled | Native text uses ASCII whitespace; Unicode parity unresolved |
| Worker independent of UIKit lifetime | Partially implemented / blocked standalone route | No launchd service; UIKit may suspend without active audio and termination interrupts work |
| Archive backup | Partially implemented | Opaque archive storage/transfer; no multi-file snapshot, delta or retention policy |
| GhostCode/local integration API | Verified on Windows + simulator | Authenticated job/status/events/SSE/artifact flow; no GhostCode database/repository modified |
| Direct-media provider abstraction | Verified in simulator | HTTPS/connectivity/YouTube-web-page rejection tests |
| Native AVPlayer handoff | Partially implemented | Play request only; missing result/error handshake and PCM coordination |
| Independent YouTube playback | Not implemented | No extraction, DRM or unsupported authentication workaround |
| Memory pressure scheduling | Verified in simulator; native compiled | No actual device pressure/thermal/Jetsam test |
| Actual native RSS | Verified on physical iPhone, short samples only | New fifteen-sample range 11,337,728–11,395,072 bytes; older range preserved separately. Not long-term/leak certification |
| Windows tray | Compiled but not visually verified | Self-contained WinForms output; interactive visual acceptance pending |
| iOS ARM64 build/sign/package | Verified as build artifacts | Zig Clang + LLD 23.1.5 + real SDK; ARM64/min12; ad-hoc integrity, scoped Debian directories; runtime assessed separately |
| iOS install/Upgrade/data preservation | Verified on physical iPhone historically | Normal installer succeeded and sentinel survived Upgrade; newest repair build has not been deployed |
| SSH / temporary USB forwarding | Verified on physical iPhone historically | Existing USB SSH port 44 and strict isolated host-key verification; credentials excluded from deliverables |
| Native logs and crash profiling | Partially verified | Bounded own-app logs and RSS; extended crashes/Jetsam/unplug/thermal/battery tests still pending |

See [current hardware evidence](HARDWARE_TEST_RESULTS.md), [review and repair tracking](REVIEW_2026-11-18.md), [next steps](NEXT_STEPS.md) and current `repairs-*` reports. Earlier `continuation-*` failures are retained as dated history; they are not the final entry status.
