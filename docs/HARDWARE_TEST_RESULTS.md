# Hardware test results — 2026-11-18

## Native UI 2.0 session — current results, 2026-11-18

Target confirmed iPhone7,2 / ARM64 / iOS12.5.8. Existing paired TLS pin passed; port44 exposed Dropbear, port23451 served the new embedded Agent. No SSH login or system change was used in this mission. Paired `com.apple.mobile.installation_proxy` returned `InvalidService`; user subsequently installed the IPA through their own method. Exact installer mechanics were not observed.

New native hello advertises apps/ui-inspect/temporary-wifi-tls. Protocol-only checks: 13/13. Native inspection: **40/40 with 10 real own-view PNGs**, 667×375 points; phone user confirmed the new UI. Initial attempts hit USB disappearance and a foreground guard; a bounded retry after reconnect passed. These PNGs are actual UIKit rendering, not computer-generated layout sketches; media artwork is real PC metadata. Touch swipes, animation frame rate and bright-video contrast remain unmeasured.

Three-second PCM fixture: **24/24**, 301 frames, gain0.15, routeSpeaker, 48kHz, AudioQueue running/primed, getters/noErr, peaks4096, sampled silence/overflow/gaps0, sender drops0. System volume was observed about0.8366 and was not changed. Human audibility of this particular test was not confirmed. RSS before/after24,252,416/24,498,176 bytes. [Full report](reports/ui2-physical-pcm.json).

Temporary Wi-Fi: explicit selected private Wi-Fi interface, 15-minute enrollment, manual switch policy. Phone acknowledged enrollment; no connected TLS ownership within25s. Test revoked the session. No firewall, trust store or network change. Do not attribute this conclusively to client isolation, firewall or native TLS. [Failure report](reports/ui2-wifi-physical.json).

Final observed Host: physical USB connected, Wi-Fi off, notification collection unavailable/disabled, audio streaming intentionally off for UI QA. See [final status](reports/ui2-host-final-status.json). Old audio-quality failures remain below and have not been waived.

## Historical evidence follows

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

**Earlier pre-reopen checkpoint (historical): first audible iPhone PCM playback had not passed.** The user reported “No sound” in both short fixture tests, at queue gains 0.08 and 0.25. System output volume remained 0.5. The user also reported a black screen, then paused device work to independently service the system. No firmware/system/jailbreak work was performed by this agent. The subsequent successful recheck/Upgrade/audio confirmation is recorded in the latest checkpoint above. The user has now unplugged the device.

## Scope and historical environment

Target observed before pause: iPhone 6 (iPhone7,2), ARM64, iOS 12.5.8, about 1 GiB physical RAM. Windows 10 build 19045 reached it through the existing Apple Mobile Device Service. Existing paired lockdown TLS used exact paired-device certificate pinning. Root SSH was available at USB port 44; device ports 22/2222 refused. AppSync Unified 116.2, dpkg, uiopen and uicache were already present in the user-prepared environment. BackThread did not install, repair or restart those components.

No macOS/Xcode/Theos/usable WSL was available. Existing Rust toolchain's LLVM LLD 23.1.5 plus Zig 0.13/Clang 18.1.6, real iPhoneOS 12.4 SDK and rcodesign 0.29 produced the working recorded app. Original multi-document SDK TBDs are used with legacy dyld metadata. LLD C entry and UIKit probes actually executed; the full UIKit application then served the embedded Agent. This is not a controlled proof that every earlier failure was a Zig linker defect: ordinary URL launch also waited for an iOS confirmation the user could not see. A temporary own-app probe used the existing normal app-launch service, fixed to BackThread's identifier, with no force-unlock/security options.

Standalone Agent execution from its owned /var/mobile/BackThread path remained denied (Killed: 9). That is distinct from the now-proven embedded Agent. No trust cache, AMFI, activation, sandbox, launchd or system framework was altered to overcome it.

## Physical results actually obtained

| Exercise | Observed result | Evidence |
|---|---|---|
| Full native protocol | Real hello simulated:false; correct-key auth, wrong-key rejection, malformed-version close/recovery, heartbeat, events/dedup and new socket authentication | [protocol](reports/continuation-launcher-native-protocol.json) |
| Native files/worker with PCM | One-MiB upload, full SHA-256, exact range and memory jobs succeeded concurrently with actual PCM | [first fixture](reports/continuation-first-native-pcm.json) |
| First 3-second fixture | Queue callbacks/noErr/Speaker; 69,120 silence bytes and 57,600 overflow bytes observed; user heard no sound | [fixture](reports/continuation-first-native-pcm.json), [listening](reports/continuation-listening-checkpoint.json) |
| Second 3-second fixture | 283 packets, 543,360 bytes; input/render peak 4096, queueGain 0.25, hardware 48 kHz, Speaker, zero sampled ring overflow/underrun/gaps; user still heard no sound | [peaks](reports/continuation-native-pcm-peaks.json) |
| WASAPI + worker, requested 60 seconds | 5,881 sender frames, 5 sender drops; receiver 5,876 packets, 4 gap events, 0 overflow, 410,880 total silence bytes. The old poll loop lasted 62.25 seconds; total includes tail after sender cancellation | [one-minute report](reports/continuation-native-wasapi-60s.json) |
| Known Windows signal through USB | Windows quiet fixture was captured through WASAPI; receiver input/render peak 446 and 147,848 nonzero rendered samples; 0 sequence gaps/overflow; no acoustic confirmation | [native result](reports/continuation-native-wasapi-known-signal.json), [Windows fixture](reports/continuation-paired-windows-known-signal.json) |
| Native RSS during one-minute run | Twelve memory-job samples 11,845,632–12,111,872 bytes (11.30–11.55 MiB); no extended leak/budget certification | [one-minute report](reports/continuation-native-wasapi-60s.json) |
| Normal installation/Upgrade | UI 0.1.2 installed; owned probe sentinel survived container rotation; own staging cleaned. Explicit own-app uninstall/dpkg probe removal were tested earlier | Earlier continuation install/Upgrade reports |

The old one-minute report's final three samples all have 5,876 packets while silence increases from 19,200 to 410,880 bytes. The added 391,680 bytes equal 2.04 seconds at 192,000 bytes/s and cannot all be called active-stream underrun. There are also genuine earlier drops/gaps and silence increments. The corrected acceptance runner now starts its deadline at PCM pacing and excludes replies completed after cancellation; it has not been rerun physically.

## Current code versus tested device build

The native repair app including own network diagnostics was upgraded through the normal installer, reopened by the user and physically exercised. It passed 27 fixture/protocol/worker checks; getter/volume and nonzero-buffer paths executed and the user heard the fixture. Normal production Host gain 1 then produced audible Windows media with stutter. These are separate from the earlier failed deployment attempt and silence reports.

The newest Windows single-write/pooling and write-wait telemetry changes were implemented after that listening feedback and the user's unplug. Their physical quality effect remains untested. Reset/Stop and real queue-failure recovery are still not physically injected/tested, even though the new app executed. Keys/passwords/pairing data stay private and no system repair was performed. Device operations are now waiting for user reconnection.

## Outstanding physical acceptance

Stable continuous speaker output; full UIKit media/touch/artwork beyond the observed connection label; phone-to-Windows commands; interruption/route/screen-off behavior; queue-failure recovery; job cancel/resume/quota/restart; physical unplug/replug; long mixed workload; crashes/Jetsam/thermal/battery and acoustic latency. App/background lifetime without active audio is not guaranteed. No new phone test was run during offline review/repair.

See [FEATURE_STATUS.md](FEATURE_STATUS.md), [PERFORMANCE.md](PERFORMANCE.md), [DEVICE_ACCEPTANCE.md](DEVICE_ACCEPTANCE.md) and [NEXT_STEPS.md](NEXT_STEPS.md). `continuation-*` reports preserve earlier observations; current `repairs-*` reports describe offline repairs only.
