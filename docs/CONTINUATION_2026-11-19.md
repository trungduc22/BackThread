# UI2 continuation and upstream audit

## UI2 preservation build 7 — 2026-11-19

Fetched origin/master at9c5fdac, including774bf8d and the alternate hybrid-media/Phase9 history. It diverges from136d1ae and is not a fast-forward: it uses a different UIKit architecture and does not contain the Apps/drawer/temporary-TLS implementation from the verified local UI2 checkpoint. Remote history is retained; it has not been reset or merged wholesale. This build continues136d1ae to preserve the user's preferred layout. New upstream media/telemetry/audio-routing features have NOT been integrated into this build.

Build7 keeps the original artwork, compact transport and retained pages/drawer; adds a quiet24-hour clock with seconds and real iPhone battery/charging state at the top right; places native iPhone system-volume control directly on Media; makes every displayed notification a top bar, including critical, with readable body and dismiss action. Old fullscreen preferences are ignored/removed from Settings. IP placeholder contrast was repaired. Native receiver start and ownership/stop now share the same lock to close the reviewed handoff race.

ARM64/minimum iOS12 build and22Python tests (including9artifact tests) pass;27 shared interaction decisions pass. Static analyzer completed all17units clean; see reports/build7-native-analysis.json. No new on-device acceptance: the current USB audit found no uniquely connected device. Historical40UI/24PCM passes apply to build5, not build7. No firmware/system/installer repair performed. PC CPU/RAM/battery/radio telemetry, three audio destinations, real Windows toast copying, physical Wi-Fi and full alternate-media integration remain open. The old strict simulator soak failure remains visible.

IPA: artifacts/ui2-build7/ios/BackThread.ipa, version0.2.0/CFBundleVersion7. Do not confuse origin's build6 or the earlier build5 packages with this artifact. Native UI must be captured on the phone again with scripts/test-native-ui.py; both normal and critical now assert fullscreen=false. Windows source/binaries are unchanged in this checkpoint.
