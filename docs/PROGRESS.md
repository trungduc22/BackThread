# BackThread progress — 0.1.2

## Latest verified hardware checkpoint — 2026-11-18

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
