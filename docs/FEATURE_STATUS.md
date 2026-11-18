# Feature acceptance matrix — 0.1.2

## Build8 — status, artwork and real Windows notifications, 2026-11-19

Native: custom rounded battery badge with percentage inside and charging/low-power/low-battery colors; clock retained; Wi-Fi interface/address status and current headphone/Bluetooth audio route name. No private APIs, SSID entitlement workaround or Bluetooth scanning. BT dash means no reported Bluetooth audio route, not radio off; Wi-Fi address presence is not proof of Internet access.

Artwork now uses a bounded32×32 RGBA sample and512-bin color histogram. Main and distinct secondary actual colors form a gradient. On track/source/artist change it stays brighter2s then fades3s to darker values of those same hues. Solid artwork correctly keeps one hue; missing/transparent artwork has a neutral fallback. Reduce Motion disables the fade. No per-second artwork processing or live blur. Palette edge cases pass native C tests.

The in-app banner is now neutral translucent charcoal, rounded, with source/title/body and swipe/close dismissal. It remains nonmodal for every priority. Build8 ARM64/iOS12 links14 production units, analyzer17units clean;23Python (including9artifact and transient-lock regression),33managed,27shared interaction cases and palette tests pass. Windows report saves now retry only bounded transient sharing/permission errors; permanent failures still propagate.

Windows fix: real self-contained MSIX declares package identity/userNotificationListener, with a foreground setup/test form. Manifest namespaces from the upstream packaging template were repaired and actual MakeAppx validation/signature verification passed. First CurrentUser trust attempt failed0x800B0109 and was rolled back. User explicitly authorized LocalMachine TrustedPeople; exact certificate315BABA9786C508E87D63D5F7547D5F0712268D7 was added there, package0.2.0.8 installed, and packaged Host activated with the existing private config. No TrustedRoot, DeveloperMode, execution-policy or WindowsApps ACL change. Development private signing PFX was deleted. Public certificate expires2026-12-18; production/stable update signing remains separate work.

Final runtime is packaged Tray (PID in artifacts/ui2-build8/packaged-tray.pid), USB connected, existing key/config, streamAudio=false, collection opt-in=true, Windows permission Allowed. Old portable Host was stopped by exact PID/path. A genuine owned Windows toast was collected, and its exact win-ID was visible in the foreground iPhone banner; captured own native view SHA verified. Content stayed masked per existing privacy settings. This is real OS-to-phone delivery, not direct API event injection. Only owned test-event metadata was recorded.

The phone was still running its currently installed client during this delivery proof; build8 clock/battery/palette/banner styling has NOT been installed or visually accepted on hardware. User handles IPA installation because Installation Proxy is unavailable. Prior strict audio-soak failure, physical Wi-Fi limitation and pending three-way output routing remain unchanged.

## UI2 preservation build 7 — 2026-11-19

Fetched origin/master at9c5fdac, including774bf8d and the alternate hybrid-media/Phase9 history. It diverges from136d1ae and is not a fast-forward: it uses a different UIKit architecture and does not contain the Apps/drawer/temporary-TLS implementation from the verified local UI2 checkpoint. Remote history is retained; it has not been reset or merged wholesale. This build continues136d1ae to preserve the user's preferred layout. New upstream media/telemetry/audio-routing features have NOT been integrated into this build.

Build7 keeps the original artwork, compact transport and retained pages/drawer; adds a quiet24-hour clock with seconds and real iPhone battery/charging state at the top right; places native iPhone system-volume control directly on Media; makes every displayed notification a top bar, including critical, with readable body and dismiss action. Old fullscreen preferences are ignored/removed from Settings. IP placeholder contrast was repaired. Native receiver start and ownership/stop now share the same lock to close the reviewed handoff race.

ARM64/minimum iOS12 build and22Python tests (including9artifact tests) pass;27 shared interaction decisions pass. Analyzer completion is recorded separately. No new on-device acceptance: the current USB audit found no uniquely connected device. Historical40UI/24PCM passes apply to build5, not build7. No firmware/system/installer repair performed. PC CPU/RAM/battery/radio telemetry, three audio destinations, real Windows toast copying, physical Wi-Fi and full alternate-media integration remain open. The old strict simulator soak failure remains visible.

IPA: artifacts/ui2-build7/ios/BackThread.ipa, version0.2.0/CFBundleVersion7. Do not confuse origin's build6 or the earlier build5 packages with this artifact. Native UI must be captured on the phone again with scripts/test-native-ui.py; both normal and critical now assert fullscreen=false. Windows source/binaries are unchanged in this checkpoint.

## Current checkpoint: Native UI 2.0, 0.2.0 build 5 — 2026-11-18

This section supersedes historical assessments below. Continued from fetched HEAD `6b986ce`; no newer remote commit was found. The user installed the IPA independently after paired Installation Proxy returned `InvalidService`. No system repair was attempted.

| Area | Current evidence and boundary |
|---|---|
| Native UI 2.0 | Real iPhone: 40 automated inspection checks, 10 SHA-verified native-view PNGs, six retained pages, drawer, compact/critical notifications and restoration. User saw the new dark UI/corner grip. Touch gestures, FPS and accessibility hardware acceptance remain pending. |
| Physical protocol/worker | 13 checks passed without audio on the new native Agent. |
| Physical PCM fixture | 24 checks passed, 3 seconds, gain 0.15, Speaker route, noErr, nonzero buffers; sampled gaps/drop/silence zero, sender drops zero. No new listening confirmation: speaker audibility is not inferred from counters. |
| Memory | New UI protocol RSS 23,080,960–23,146,496 bytes; after opening all pages, PCM before/after RSS 24,252,416–24,498,176 bytes. Bounded samples, not a leak or video profile. |
| App launcher | Local pinning GUI/catalog, real bounded icons, ID-only requests, fixed arguments and execution feedback implemented. Windows fixture launch tested. Real phone grid had no pins; phone touch-to-launch and packaged activation not accepted. |
| Wi-Fi | Windows TLS/pin/HMAC/expiry/rejection tests pass. Real phone acknowledged USB enrollment; TLS handover timed out in 25 seconds. Session revoked, USB retained. Cause not isolated; physical Wi-Fi is not accepted. |
| Windows notifications | Portable Host reports package identity/capability unavailable. Normal Windows notification copying has NOT been verified. Authenticated events API and native presentation are separate. |
| Build/regression | Managed 33/33, Python 22/22, native protocol/ring and 27 shared interaction decisions pass. ARM64/iOS12 app links 13 units; analyzer clean across 16 including probe; nine artifact checks and self-contained portable smoke pass. |

Latest user request changes the design: **all notifications become a top bar**, including critical events. Add a prominent clock, PC/iPhone batteries, Wi-Fi/Bluetooth, PC CPU/RAM, subtle living motion/special notification treatment, PC-only/phone-only/both routing and inline volume. These changes are **requested, not implemented** at this checkpoint. Do not accept the current fullscreen screenshot as the new requirement.

The earlier strict 120-second simulator soak still failed with 9,600 silence bytes. Earlier short ordinary WASAPI listening success remains historical 0.1.2 evidence. No new soak, 30-minute acceptance, native direct-video acceptance or provider integration is claimed. See [current handoff](HANDOFF_ANTIGRAVITY.md), [UI inspection](reports/ui2-native-ui.json), [PCM](reports/ui2-physical-pcm.json), [Wi-Fi failure](reports/ui2-wifi-physical.json).

## Historical 0.1.2 evidence follows

## Resumed build and physical playback — 2026-11-18 evening

Fetched every branch/tag from GitHub before continuing: only `master` at `307e167` existed, with the six original commits. No additional Antigravity commit was present on this remote. Continued from that full history. New implementation checkpoint: `6c2fb35`.

**Ordinary Windows audio was heard continuously on the real iPhone in this short test.** The user answered “Có tiếng, nghe liền mạch” during a 60-second WASAPI run. All 32 physical functional checks passed; 6,000 PCM frames were sent. Sampled native receiver gaps, overflow, silence and sender drops were zero. App gain was 0.25; observed phone system volume about 0.609, unchanged by BackThread. This is a bounded listening/profile result, not a 30-minute acceptance or an acoustic latency measurement. Windows capture still recorded 17,416 zero-filled bytes; their timing was not localized, so this is not an all-stage zero-loss claim.

Ten native RSS samples ranged from 13,082,624 to 13,185,024 bytes (12.48–12.57 MiB). The existing paired TLS certificate was pinned successfully. USB was briefly absent, then protocol checks passed after the user reconnected. This was not a controlled Host cable-recovery acceptance test. No app reinstall, system repair, firmware or jailbreak change was performed.

The new diagnostics separate pacing, queue residence, socket writes, capture zero fill and receiver handler delays. An instrumented simulator failure showed a 67.141 ms receive-handler wait and 73.286 ms packet-arrival gap while sender writes stayed at or below 1.102 ms. File begin/append/read are now processed in a bounded serial two-slot queue, following the native architecture, so file I/O no longer holds the simulator PCM/control reader. A forced 3-second file delay regression verifies ping responsiveness and exact returned bytes. Buffers and quality thresholds were not enlarged or relaxed.

Managed functional suite: **28/28 pass**, Release build: **0 warnings/errors**. Final simulator 120-second audio/4-MiB worker soak: **FAIL**, 176 cycles, 0 sender drops, 0 gaps, 0 overflow bytes, 9600 silence bytes. Failed baseline and instrumented runs are retained separately; do not substitute the short phone result for a failed simulator gate or vice versa.

Fresh native ARM64/minimum-iOS-12 build and nine artifact checks passed. `install-ios.py --build-dir` and `BACKTHREAD_IOS_BUILD_DIR` select the exact custom build for IPA preparation/tests; defaults remain compatible. Native executable sources are unchanged, so rebuilt packages were not redeployed unnecessarily. YouTube/Spotify independent playback remains unimplemented/unverified; there is no new provider feature in this checkpoint.

Fresh published Host/Simulator API, file integrity, SSE and process-restart smoke passed. Native analyzer completed all 10 translation units clean; 9 device-tool tests, 4 analyzer-tool tests and the shared C protocol/ring regression passed. All five rebuilt native manifest artifact hashes match the earlier native build exactly. The published Host was reconnected to the physical iPhone with ordinary WASAPI and app gain 0.25, matching the listening test. Private config/key remain local.

Evidence: [validation](reports/resume-validation.json), [physical profile](reports/resume-physical-wasapi60.json), [human listening](reports/resume-human-listening.json), [functional tests](reports/resume-tests-final.json), [final soak metrics](reports/resume-soak-file-io-metrics.json). Prior evidence below is historical and remains intact.

## Earlier checkpoint — before this continuation

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
