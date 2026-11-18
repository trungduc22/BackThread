# BackThread progress — 0.1.2

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

2026-11-18, Asia/Saigon. Continued from 0e9f88c while preserving prior Windows/simulator behavior and repository history. Current acceptance remains incomplete.

## Actual device progress before pause

- Normal IPA install/Upgrade and data preservation succeeded in the existing user-prepared environment. Corrected Debian directory headers were validated by own probe installation/removal.
- LLD 23.1.5 with original iPhoneOS 12.4 stubs produced actual C/UIKit entry and the full embedded Agent. Ordinary own-app launching resolved the invisible URL-confirmation obstacle. Standalone execution remained separately denied; no execution policy changed.
- Physical authentication, framing rejection/recovery, heartbeat, events/dedup, new socket, one-MiB upload/SHA/range and named memory jobs passed. Nonzero Windows WASAPI audio reached native AudioQueue buffers.
- Both human listening checks returned no sound. Physical screen was reported black. Native short-run RSS was sampled; extended stability, UI/touch and acoustic results are unverified.
- User paused device work while independently maintaining the system. All subsequent repair work is offline; no firmware/system/jailbreak changes were made. The prior device state must be checked anew only after readiness.

## Offline repairs after review

- Native queue failure now reports audio.error and stops/discards that stream while preserving USB control; a failed start clears the PCM acceptance flag. Host exposes audioError without intentionally reconnecting the session.
- Reset checks requested playback under the same lifecycle lock as Stop. Start/volume failures are retained/returned; queue getter status and presence are explicit.
- Acceptance timing begins at actual PCM pacing. Source cancellation and sample admission share that deadline; absolute polls skip missed slots, late replies and shutdown tail are excluded. Source failure propagates; --require-nonzero can gate a known WASAPI signal. Zero-loss observations remain separate from audible/quality acceptance.
- Windows regressions cover slow/uncancellable stats, early source termination and receiver-error/control-session isolation. Existing corrupt-artifact recovery remains tested.
- Analyzer saves a fresh incomplete/failed report before running and marks complete only after all expected sources. Report admission has dedicated tooling tests. Explicit direct interpreter invocation avoids relying on the Windows app alias; no system Python setting changed.
- Build documentation now requires the proven explicit LLD path and original SDK stubs. Current feature/hardware/limits/next-step documents distinguish recorded native execution from still-failing sound and runtime-unverified repairs.

Current build/test hashes and counts are in reports/repairs-validation.json and associated reports. Older continuation/review reports remain dated evidence. Release artifacts are engineering checkpoints with no bundled key/SDK/toolchain. No remote push is performed.

Final offline checkpoint: 25/25 functional tests, 9 artifact, 9 device-tool and 4 analyzer-tool tests passed; all ten native analyzer units clean; five native artifacts reproducible; latest portable smoke and 19-check isolated simulator acceptance passed. The corrected 120-second strict simulator audio-quality soak failed and is retained explicitly. No blanket acceptance or stutter fix is claimed.

A final Host error-isolation repair also keeps control/worker alive when audio gain/start requests are rejected, not only after unsolicited audio.error. Regression exercises both cases on the same connection with a subsequent heartbeat and job query. Normal USB Host restores app gain 1 by default, without changing phone system volume.

Dedicated-writer follow-up: latest async-writer live run had 741 sender drops after 11,269 frames while maximum observed write wall time was 33.727 ms. Scheduling before writes is a hypothesis under investigation. NetworkStream output now uses a dedicated AboveNormal thread and bounded 3-second synchronous writes; generic streams keep the async path. All 26 functional tests pass, including a paused-caller-context regression. No strict soak or physical quality result exists for this newest writer yet. Earlier archives at checkpoint 363c821 predate this follow-up. The user requested a one-file Antigravity handoff and independent provider playback planning; these providers are not implemented.
