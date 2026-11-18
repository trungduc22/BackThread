# Performance evidence — 0.1.2

## Latest verified hardware checkpoint — 2026-11-18

**First audible Windows PCM through the iPhone speaker is now verified.** After the normal own-app Upgrade and foreground reopen, the user saw “Connected by USB”. The new three-second PCM fixture passed 27 physical protocol/audio/worker checks and the user answered “Có nghe tiếng”. Current fixture stats show Speaker, 48 kHz, gain 0.25, system volume 1, both peaks 4096, noErr, successful getters, and zero sampled gaps/overflow/underrun. This does not establish a single root cause for earlier silent attempts: app build, launch state and user-controlled system volume differed.

Normal WASAPI media was then heard through the iPhone, with the user reporting “Có nhưng bị ngắt/giật” after normal Host app gain was restored to 1. **Continuous audio quality is not accepted yet.** The separate 90-second diagnostic run at app gain 0.25/system volume about 0.105 received/rendered nonzero PCM but had 472 sender drops, 132 gap events, 1,416,960 overflow bytes and 2,423,040 active-window silence bytes. Fifteen native RSS samples were 11,337,728–11,395,072 bytes. It passed its functional gates, not a zero-loss/acoustic quality gate.

A normal-Host sample measured 0.046875 CPU seconds over 3.04 wall seconds and 84,193,280 bytes RSS; total Windows CPU was reported 100%, with 3,585 MiB available and no paging in that snapshot. This is evidence of load, not proof of one cause for stutter. Its last observed sender-drop counter was zero; that does not prove native receiver/capture quality.

The user subsequently confirmed physically unplugging the cable. USB disappearance is therefore an observed user disconnect, not an unexplained discovery failure. Hardware work is now waiting for reconnection. Latest Windows changes batch each frame into one pooled socket write and add write-wait telemetry; **their physical effect on stutter has not been tested**. No system/framework/service/jailbreak repair was performed.

Evidence: [human confirmations](reports/repairs-human-listening.json), [physical fixture](reports/repairs-physical-fixture.json), [90-second native profile](reports/repairs-physical-wasapi90.json), [normal Host load](reports/repairs-host-load.json). Earlier failed listening reports remain historical evidence.

## Real iPhone results before device pause

The requested 60-second WASAPI/worker run took 62.2538451 seconds in the old polling harness. It sent 5,881 frames with five sender drops; native receiver recorded 5,876 packets, four gap events, zero ring overflow, and 410,880 total silence bytes. Input was silent Windows audio in that run. Twelve during-audio RSS samples ranged from 11,845,632 to 12,111,872 bytes (11.30–11.55 MiB). These short samples do not prove a leak-free budget, CPU/battery efficiency or background lifetime.

**Do not interpret 410,880 bytes as active-stream underrun alone.** The last three samples have unchanged packet counts while silence rises from 19,200 to 410,880 bytes. That 391,680-byte increment is 2.04 seconds of zero fill after sending stops. There were genuine earlier loss/silence observations too. See [original native report](reports/continuation-native-wasapi-60s.json).

A separate known-signal run passed real Windows WASAPI → USB → native-buffer flow: peaks 446, 147,848 nonzero rendered samples, no sequence gaps/overflow; total silence includes an old-harness shutdown tail. The second three-second direct PCM fixture had peak 4096 with zero sampled overflow/underrun/gaps. Both human fixture listening checks returned no sound. No acoustic latency, continuous speaker quality or sound-pressure result exists.

## Measurement repair

Current acceptance source deadline begins at PCM pacing after audio.start. Polls use absolute times; missed deadlines are skipped. The final request is scheduled before cancellation, and replies completed outside the source window are excluded. Sample completion times and requested window duration are recorded. A slow stats/job call cannot extend source playback. Ring/sequence/sender loss observations are reported separately from the automated functional pass and human audibility. This revised harness has not been rerun on the physical phone.

## Windows/simulator regression evidence

The earlier recorded 120.350-second soak passed 223 four-MiB upload/hash cycles and 12,036 PCM packets (23,109,120 bytes), with zero measured sequence gaps, sender drops, receiver overflow and underrun silence. Test-process RSS was 92,618,752 → 98,242,560 bytes and CPU time 88.46875 seconds; these are Windows test-process values, not native process metrics. A new repair regression report records the current rerun separately. Do not merge or silently replace historical measurements.

Native C ring capacity stays 19,200 bytes (100 ms at 48 kHz PCM16 stereo), initial priming 5,760 bytes (30 ms), three output buffers of 1,920 bytes. These are buffer sizes, not measured acoustic latency. No buffer-size or scheduling retune was justified by the invalid tail-inclusive figure. Windows timer resolution is scoped to active streaming and can affect power use.

## Still required

Physical speaker confirmation; corrected active-window measurements; queue-failure/reset/route/interruption recovery; at least 30 minutes simultaneous audio/media/events/jobs/file work; physical unplug/replug; native CPU/RSS over time; own crash/Jetsam evidence; thermal/battery and screen-off behavior. Device work remains paused and system services/policies remain untouched.

## Repair validation caveats

The first repaired 120-second soak failed under concurrent build/analyzer load (33 sender drops, 11 gaps, 1,920 overflow bytes, 82,560 silence bytes). A following isolated run had zero sender drops/gaps/overflow but 3,840 silence bytes and failed the strict zero-loss gate. Both used the earlier final-stats-after-stop harness; neither is silently converted to a pass. The final harness now samples before stopping the source and records bounded loss-change samples. Its latest result is recorded separately in repairs-soak-final.json. A passing simulator run would still not override the measured physical stutter.

The short normal-Host CPU snapshot was taken near the user's cable disconnect; it is not a certified steady-state active-playback CPU figure. Total CPU saturation and small own-process CPU in that snapshot do not identify a unique stutter cause. The normal Host's zero sender-drop observation also does not certify capture/receiver quality.

The final corrected 120.326-second simulator soak **failed** its unchanged zero-loss gate: 118 sender drops, 20 gap events, 5,760 overflow bytes, 243,840 silence bytes, 98 four-MiB cycles, peak queried ring 19,200 bytes. Final stats were taken before source stop, so this remaining loss cannot be dismissed as shutdown tail. Ten bounded loss-change observations were recorded. Latest Windows batching is therefore a candidate optimization with functional/ownership checks, not a demonstrated stutter cure or a passing strict quality result. CPU/load conditions were not controlled enough for a causal before/after comparison.
