# Next steps — 0.1.2

## PhoneBox and NativeShellKit

Resume the [Antigravity continuation](PHONEBOX_CONTINUATION.md) from its recovered source once available. HiberDesk and BackThread remain separately launched applications.

## Audio follow-up

Instrument the same PCM sequence on the writer and receiver around an underrun. The soak still fails despite confirmed isolation from job journal I/O. Preserve the current app until the user authorizes installation. See [results and verification commands](AUDIO_JOURNAL_FOLLOWUP.md).

## Foundation checkpoint

See the [engineering report and acceptance order](NATIVE_FOUNDATION_REPORT.md). Prioritize timing correlation and isolated audio soak before resuming the workbench UI.

## Build10 continuation — 2026-11-19

Native0.2.0(10) adds a monochrome blurred battery, native LIVE label (explicit/indefinite sources only), artwork swipe next/previous without page-gesture theft, extensionless signed Googlevideo stream support and native playback diagnostics. Adds a foreground app-private SSH command inbox (open/status/play/pause/seek/return-pc), without a new listener/daemon. Existing UI2 layout and installed Host9 are preserved.

A real optional yt-dlp/EJS metadata-only run on Windows resolved the public YouTube sample to a single H.264/AAC360p30 stream. Windows HEAD returned200/video/mp4 without a media body. This is NOT phone playback or phone-local resolution. The connected physical client lacked build10 verification capability; the new test refused before opening video. SSH44 answered a read-only Dropbear banner check; authentication/inbox execution was not attempted. No phone system, activation, jailbreak or firmware changes.

Verification:35managed checks,31Python checks,9checks against the new native artifacts,19native analyzer units clean. IPA prepared, not installed. Native visual changes, audible YouTube playback, thermal benefit, hardware decoding and PC-disconnect survival remain unverified. Historical strict simulator soak failure remains open. Mail/OTP providers, personal Messenger/Zalo access, build-console UI and a daily scheduler are not implemented. See [Build10 notes](BUILD10_NOTES.md), [validation](reports/build10-validation.json), and [companion/automation design](COMPANION_WORKSPACE.md).


Immediate next gates: install the exact build10 IPA using the user's chosen method; keep BackThread foreground and run scripts/test-native-direct.py with a freshly resolved private URL. Require H.264/display readiness/time advancement/network activity and stopped PC PCM, then verify PC ownership/audio recovery. Do not claim success from metadata/HEAD. Test the SSH inbox separately with existing verified trust. Then design a bounded Build/Run log feed and a read-only scheduled job; do not enable account actions or claim unattended background execution.

## Build9 continuation — 2026-11-19

User requested left-aligned clock/battery, no seconds, Wi-Fi/Bluetooth icons, PC CPU/RAM bottom right, faster progress, volume faded until interaction, YouTube Next repair and restored phone audio. Later instructions add a brighter progress tint from the artwork palette, continuous palette transition including idle, a4second bright hold and a slightly brighter final dark palette.

Implemented in native build9: left HUD with HH:mm and battery, original Wi-Fi/Bluetooth vector marks plus headphone route name; noninteractive lower-right PC metrics label below drawer; reserved page-header space. Progress interpolates at10Hz between Host anchors; native video observer also10Hz. Volume stays at18% opacity, becomes opaque during pan/tap and fades1.2s after release; native controls never auto-hide during volume adjustment. Artwork uses the actual current presentation colors when interrupted, transitions to the new two-color palette in about1s, holds4s and dims over3s. Empty media also transitions. Final dark gain increased18% to24%; progress/thumb use a brighter mix of the same palette. Reduce Motion retains the static dim state.

Host9 samples real Windows GetSystemTimes CPU deltas and GlobalMemoryStatusEx physical RAM and sends them inside media.state.system; first CPU reading is unknown until a second sample. GSMTC position uses LastUpdatedTime and playback rate so old timeline anchors do not reset the phone once per second. A Windows metrics sampling test passed.34managed and23Python checks pass; native compile,17-unit static analysis and9artifact checks pass. These do not establish build9 visual acceptance on the phone.

Audio root cause was the QA streamAudio=false config left in use by Codex. Restored true and restarted only the owned packaged Host8 with the same key/config/gain. User confirmed sound returned. A later status showed112,892frames, no capture/audio error, two sender drops accumulated under build load. This is not a zero-loss or new long-duration audio acceptance. Windows notification permission remains Allowed/collection enabled. No system volume, iPhone activation, firmware or jailbreak component changed.

YouTube diagnosis: Thorium CanNext=false but TrySkipNextAsync returns true; the title did not change even after4s. Native build9 no longer disables Next solely from that flag, but OS acceptance alone is NOT video-switch success. Prepared extensions/youtube-next: a site-scoped Media Session nexttrack handler clicks only YouTube's enabled visible Next button; removes itself on ads/non-watch pages. No network, credentials, native messaging or stream extraction. Node fixture tests cover action dispatch, unavailable/disabled button, ads, navigation and unload. User explicitly authorized the helper, loaded it in Thorium, and reloaded YouTube. CUA observed the enabled extension. A subsequent Windows probe had CanNext=true, command accepted, trackChanged=true and position reset to0.728s. The user then confirmed the physical iPhone Next button changed the PC video. The helper falls back to an ordinary visible YouTube recommendation when the player omits Next; only HTTPS youtube.com/watch links for a different video are allowed, promoted entries and playing ads excluded.

Host9 MSIX0.2.0.9 is signed and prepared. Old build8 used a one-time signing key that was deleted after signing. A stable code signing identity was prepared in CurrentUser/My, with private key protected by Windows and public thumbprint80C95C2BCEE1265893C5F69AAFEC0A3D81EED1C6, valid to2027-11-18. The builder reuses one unambiguous matching personal-store identity, or requires an explicit thumbprint if several exist. User explicitly approved the stable signing trust replacement and Host9 update. Package0.2.0.9 is installed/running with the existing audio+notification config; old certificate315BABA9786C508E87D63D5F7547D5F0712268D7 was removed from machine TrustedPeople, new80C95C2BCEE1265893C5F69AAFEC0A3D81EED1C6 is present. No TrustedRoot, DeveloperMode, execution-policy or WindowsApps ACL change. Real CPU/RAM samples are available at the authenticated local /v1/metrics endpoint and are being sent to the phone.

IPA and signed MSIX are under artifacts/ui2-build9 and exported to ../BackThread-UI2-Build9. The user handles IPA installation. Do not report build9 phone UI or PC telemetry display accepted until that matching build is installed/tested. Historical strict simulator audio-soak failure remains unresolved. See the public test reports for exact scopes.

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

## Current continuation order — user requested handoff, 2026-11-18

1. Read [HANDOFF_ANTIGRAVITY.md](HANDOFF_ANTIGRAVITY.md) first and continue the current local commit. Do not reset to an old GitHub checkpoint; new work has not been pushed.
2. Implement the latest user request: top-only notification bar; useful clock and device/connectivity/PC usage strip; restrained living motion and distinctive special banner; PC-only/phone-only/both audio destinations and inline volume. Existing fullscreen acceptance is superseded.
3. Finish the supported Windows notification permission/identity path and prove real Windows toast-to-iPhone copying. Portable listener currently reports unavailable. Do not substitute the events fixture.
4. Investigate possible audio-owner/start race identified in handoff before adding routing. Preserve capture/receiver gains, queue thresholds, protocol/worker and the old strict soak gate.
5. Diagnose the physical Wi-Fi timeout without changing firewall, school network or iPhone system. Native enrollment succeeded; TLS connection did not.
6. Run the native UI script against the final build; obtain app-grid populated/touch-launch evidence, video/loading/seek/gesture/RSS and disconnect acceptance. New features require a fresh IPA and independent user installation if Installation Proxy remains unavailable.
7. Validate real WASAPI/listening after routing, then 30-minute load/interruption/reconnect tests. Keep the historical 120-second simulator failure and Windows capture zero-fill finding visible.

The current 0.2.0 build5 has 13 protocol, 24 PCM and 40 UI checks on hardware, not complete product acceptance. User asked to stop active development and hand off because quota is low.

## Historical plan follows

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
