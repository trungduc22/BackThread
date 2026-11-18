# Next steps — 0.1.2

## Resumed build and physical playback — 2026-11-18 evening

Fetched every branch/tag from GitHub before continuing: only `master` at `307e167` existed, with the six original commits. No additional Antigravity commit was present on this remote. Continued from that full history. New implementation checkpoint: `6c2fb35`.

**Ordinary Windows audio was heard continuously on the real iPhone in this short test.** The user answered “Có tiếng, nghe liền mạch” during a 60-second WASAPI run. All 32 physical functional checks passed; 6,000 PCM frames were sent. Sampled native receiver gaps, overflow, silence and sender drops were zero. App gain was 0.25; observed phone system volume about 0.609, unchanged by BackThread. This is a bounded listening/profile result, not a 30-minute acceptance or an acoustic latency measurement. Windows capture still recorded 17,416 zero-filled bytes; their timing was not localized, so this is not an all-stage zero-loss claim.

Ten native RSS samples ranged from 13,082,624 to 13,185,024 bytes (12.48–12.57 MiB). The existing paired TLS certificate was pinned successfully. USB was briefly absent, then protocol checks passed after the user reconnected. This was not a controlled Host cable-recovery acceptance test. No app reinstall, system repair, firmware or jailbreak change was performed.

The new diagnostics separate pacing, queue residence, socket writes, capture zero fill and receiver handler delays. An instrumented simulator failure showed a 67.141 ms receive-handler wait and 73.286 ms packet-arrival gap while sender writes stayed at or below 1.102 ms. File begin/append/read are now processed in a bounded serial two-slot queue, following the native architecture, so file I/O no longer holds the simulator PCM/control reader. A forced 3-second file delay regression verifies ping responsiveness and exact returned bytes. Buffers and quality thresholds were not enlarged or relaxed.

Managed functional suite: **28/28 pass**, Release build: **0 warnings/errors**. Final simulator 120-second audio/4-MiB worker soak: **FAIL**, 176 cycles, 0 sender drops, 0 gaps, 0 overflow bytes, 9600 silence bytes. Failed baseline and instrumented runs are retained separately; do not substitute the short phone result for a failed simulator gate or vice versa.

Fresh native ARM64/minimum-iOS-12 build and nine artifact checks passed. `install-ios.py --build-dir` and `BACKTHREAD_IOS_BUILD_DIR` select the exact custom build for IPA preparation/tests; defaults remain compatible. Native executable sources are unchanged, so rebuilt packages were not redeployed unnecessarily. YouTube/Spotify independent playback remains unimplemented/unverified; there is no new provider feature in this checkpoint.

Fresh published Host/Simulator API, file integrity, SSE and process-restart smoke passed. Native analyzer completed all 10 translation units clean; 9 device-tool tests, 4 analyzer-tool tests and the shared C protocol/ring regression passed. All five rebuilt native manifest artifact hashes match the earlier native build exactly. The published Host was reconnected to the physical iPhone with ordinary WASAPI and app gain 0.25, matching the listening test. Private config/key remain local.

Evidence: [validation](reports/resume-validation.json), [physical profile](reports/resume-physical-wasapi60.json), [human listening](reports/resume-human-listening.json), [functional tests](reports/resume-tests-final.json), [final soak metrics](reports/resume-soak-file-io-metrics.json). Prior evidence below is historical and remains intact.

## Current next actions

1. Use this implementation checkpoint and the new package manifest; older ZIPs predate these diagnostics and file-I/O isolation.
2. Run at least 30 minutes of physical WASAPI plus worker traffic, then screen-off, interruption/route and controlled unplug/replug checks. Preserve human listening and native counters separately.
3. Locate the Windows capture zero-fill interval before making a capture-start/rebuffering change. Do not infer a completely loss-free source from receiver counters alone.
4. If the final simulator gate above failed, inspect the recorded per-stage maxima before further scheduling changes; keep the zero-loss threshold.
5. Independent direct-media/provider coordination remains a separate unfinished requirement. Do not assume YouTube/Spotify compatibility on iOS 12.

## Earlier checkpoint — before this continuation

**First audible Windows PCM through the iPhone speaker is now verified.** After the normal own-app Upgrade and foreground reopen, the user saw “Connected by USB”. The new three-second PCM fixture passed 27 physical protocol/audio/worker checks and the user answered “Có nghe tiếng”. Current fixture stats show Speaker, 48 kHz, gain 0.25, system volume 1, both peaks 4096, noErr, successful getters, and zero sampled gaps/overflow/underrun. This does not establish a single root cause for earlier silent attempts: app build, launch state and user-controlled system volume differed.

Normal WASAPI media was then heard through the iPhone, with the user reporting “Có nhưng bị ngắt/giật” after normal Host app gain was restored to 1. **Continuous audio quality is not accepted yet.** The separate 90-second diagnostic run at app gain 0.25/system volume about 0.105 received/rendered nonzero PCM but had 472 sender drops, 132 gap events, 1,416,960 overflow bytes and 2,423,040 active-window silence bytes. Fifteen native RSS samples were 11,337,728–11,395,072 bytes. It passed its functional gates, not a zero-loss/acoustic quality gate.

A normal-Host sample measured 0.046875 CPU seconds over 3.04 wall seconds and 84,193,280 bytes RSS; total Windows CPU was reported 100%, with 3,585 MiB available and no paging in that snapshot. This is evidence of load, not proof of one cause for stutter. Its last observed sender-drop counter was zero; that does not prove native receiver/capture quality.

The user subsequently confirmed physically unplugging the cable. USB disappearance is therefore an observed user disconnect, not an unexplained discovery failure. Hardware work is now waiting for reconnection. Latest Windows changes batch each frame into one pooled socket write and add write-wait telemetry; **their physical effect on stutter has not been tested**. No system/framework/service/jailbreak repair was performed.

Evidence: [human confirmations](reports/repairs-human-listening.json), [physical fixture](reports/repairs-physical-fixture.json), [90-second native profile](reports/repairs-physical-wasapi90.json), [normal Host load](reports/repairs-host-load.json). Earlier failed listening reports remain historical evidence.

**Priority: stable ordinary Windows audio through the speaker.** First audible fixture and normal WASAPI media are confirmed, but media stutters. The user unplugged the cable; wait for reconnection before physical retesting. Preserve the normal gain fix and measure loss/write waits before selecting further scheduling/buffer changes.

## Immediate remaining issue

The final corrected 120-second simulator soak still fails its unchanged zero-loss gate (118 sender drops, 20 gap events, 5,760 overflow and 243,840 silence bytes). Do not rerun the same test repeatedly or declare batching a cure. Next investigation should correlate capture-ring counters, writer queue residence/wall waits and receiver arrival/buffer occupancy under a controlled host load, then compare the baseline and repaired build with the same workload. Preserve fail reports and thresholds. Confirm the latest patch on the reconnected phone before selecting further pacing or buffer changes.

## Offline repair checkpoint

The reviewed defects are repaired in source: common PCM measurement deadline with shutdown-tail exclusion; native audio failure isolated from USB control; explicit volume/getter diagnostics; reset intent checked under the lifecycle lock. The Host records unsolicited audio.error without dropping the session. Analyzer reports are invalidated before each run and cannot reuse old clean results after an incomplete invocation. See current repairs reports for actual build/test counts; these are not physical acceptance.

## Next physical regression when reconnected

1. Read-only re-enumerate the one intended device and revalidate existing paired certificate, version and services. Inspect only the BackThread app's metadata. If pairing or required services are missing, report that blocker; do not re-pair automatically or install/repair system components.
2. Through the already prepared normal installer, upgrade only BackThread-owned artifacts; preserve app data/key. Do not copy old container UUID paths blindly. Never install a release key, password or pairing record. Keep only one Agent listener on loopback 23451.
3. Verify the latest native image actually executes and serves the key-authenticated protocol. A PID, installation completion or uiopen exit status is insufficient. Do not modify standalone execution policy to work around its historical denial; the embedded app is the proven route.
4. Check audio session/category/mode, queue presence and getter/error statuses. Use at most a short controlled fixture with ordinary system volume. Compare app-local and USB input only if this will resolve a specific uncertainty. Do not repeat tones or raise volume without new evidence. Speaker playback passes only with listening confirmation.
5. Run the corrected measurement window and --require-nonzero with an intentional brief Windows signal. Keep active-stream loss distinct from shutdown tail. Profile pacing/occupancy before tuning ring size or scheduling. The passing simulator soak is not proof of sound.
6. Trigger normal app stop/start and supported audio interruptions/routes, then verify reset cannot revive stopped playback and an audio-only failure leaves ping/jobs operational. No system service restart or policy changes.
7. Validate actual UIKit media/artwork/touch, Windows command results, banners/history/privacy and allowlisted Quick Actions. Keep Windows media state authoritative.
8. Run native worker cancel/resume/quota/restart alongside audio, then at least 30 minutes mixed workload, screen-off, physical cable reconnect, RSS/CPU and own crash evidence. Do not alter Jetsam or unrelated logs/data.

## Scope to preserve

Windows Host, transport and simulator remain the foundation. Defer unrelated providers, YouTube extraction, daemon architecture and heavy dependencies. Independent unfinished features include notification permission packaging, backup retention/snapshot and AVPlayer result coordination. Report unavailable system functionality rather than repairing the user's bypass-prepared system. No firmware/activation/jailbreak operations or remote push.

Reports and local commits are the resumption point; no work continues after the execution environment ends.
