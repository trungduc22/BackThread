# Next steps — 0.1.2

## Latest verified hardware checkpoint — 2026-11-18

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
