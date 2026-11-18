# Resume iPhone integration

Continue from this native integration checkpoint. **The priority remains the first genuine PCM playback through the iPhone speaker.** The Windows Host/simulator are working, and both iOS components now compile/package. Further features are secondary.

## Current hardware gate

The attached iPhone 6 / iOS 12.5.8 is paired, reports `Activated` in the latest read-only session, and exposes Dropbear SSH on USB port 44. BackThread's standalone Agent is denied execution by System Policy. Apple's standard Installation Proxy rejects the UI's ad-hoc signature with `ApplicationVerificationFailed`, `0xe8008014`. Port 23451 remains closed. See [hardware evidence](HARDWARE_TEST_RESULTS.md).

Resume device work only with an existing authorized app installation/execution path, or a project-provided Apple signing identity and suitable provisioning profile for normal installation. Do not alter activation, firmware, trust caches, AMFI, sandbox policy, jailbreak packages or unrelated launch services. No such changes are needed to continue the independent build checks.

## First real playback sequence

1. Re-run `python scripts/inspect-ios-device.py --output artifacts/ios-device-audit.json`. Check current state; a reachable installer or root shell alone is insufficient proof that BackThread can execute.
2. Build using [BUILD_IOS.md](BUILD_IOS.md), run the artifact checks, then install only BackThread through the prepared environment's normal installer. `python scripts/install-ios.py --install` tests the standard paired AFC/Installation Proxy route for the current build; it does not bypass signature checks. Repeating the same rejected signature adds no evidence.
3. Confirm the process reaches `main` and port 23451 opens. Use either UIKit's embedded Agent or the manually started standalone Agent, one at a time. No daemon is installed. Staged files under `/var/mobile/BackThread` are earlier diagnostic copies; replace only through the reviewed BackThread installation path when usable.
4. Provision a shared fresh private session key. A matching diagnostic key already exists in ignored `artifacts/ios-private/device-session.key` and `/var/mobile/BackThread/session.key` for the standalone route. Never put its contents in logs, commits or package archives. UIKit uses its own app-container key via Pair.
5. Copy `config/example.json` to ignored `config/local.json`; use `transport: "usb"`, `streamAudio: true`, `fixtureAudio: true`, `deviceHasInternet: false`, and the matching key path. Start `BackThread.Host --config config/local.json` and confirm authenticated hello/heartbeat plus receiver stats. The real Host talks to usbmux directly; no BackThread TCP forward is needed.
6. Start with a short low-level PCM fixture. Require actual AudioQueue callbacks, increasing received bytes, `running: true`, `primed: true`, `lastOSStatus: 0`, and a speaker route. A human must confirm audibility; counters do not establish speaker sound. Record a pending listening check until confirmed.
7. Switch `fixtureAudio` to false and test actual WASAPI loopback through the phone. Check gaps, dropped/silence bytes, stop/start and reconnects before adding worker load. Record priming separately from steady-state underruns.
8. Validate UIKit media/touch/actions/events, then real file upload/hash/download and cancellation with audio active. Verify generation-fenced replies after reconnect. Collect the named `memory` diagnostic for process RSS and bounded BackThread log output.
9. Test screen lock, interruption/resume, route/volume changes, app termination/relaunch and a physical USB unplug/replug. Run a 30-minute combined workload before longer testing; collect RSS/Jetsam/crash, audio counters and reconnection results. Never disable Jetsam.

## Independent checks while the device is blocked

- Native compile/link/package, Clang analyzer, artifact/signature/package tests and reproducibility checks are documented in BUILD_IOS.md and IOS_NATIVE_AUDIT.md. Keep the target baseline ARM64 / minimum iOS 12.0.
- Run `scripts/test.ps1` after any shared/Windows changes; the corrected default is 19 managed checks plus shared C tests. `-SoakSeconds 120` explicitly opts into the additional simulator soak. Simulator outcomes must stay separate from real device acceptance.
- Native quota enforcement, Unicode word semantics, background lifetime, runtime layout and real audio timing remain unverified. Resolve issues revealed by the first actual device run before expanding features or adding launchd architecture.

The session ends with the documented signing/execution blocker; no autonomous work continues after the chat ends.
