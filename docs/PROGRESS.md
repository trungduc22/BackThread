# BackThread progress

Checkpoint date: 2026-11-18 (Asia/Saigon), continued from `738a497`. **First end-to-end iPhone playback remains blocked.** This document records observed results, not intended capability.

## Native iPhone integration follow-up

- Audited every iOS translation unit/header, shared wire C, Makefiles and app resources for iOS 12 / A8 ARM64 compatibility. Added explicit CoreGraphics/QuartzCore linkage and repaired actual warning/API/resource assumptions.
- Fixed connection stop/lifecycle races, fd-reuse response hazards with connection generations, bounded asynchronous upload I/O, active commit guards, fstat failure handling, UI null/type assumptions, memory-warning timer races and AudioQueue error/session handling.
- Prepared a workspace-local Zig 0.13.0 / Clang 18.1.6 cross-build against actual iPhoneOS 12.4 headers/stubs, ad-hoc signed with apple-codesign 0.29.0. All seven translation units compile with warnings-as-errors and pass Clang analyzer with zero final diagnostics. No SDK/tool binaries are redistributed.
- Produced baseline ARM64 / minimum iOS 12.0 UI and standalone Agent binaries, an Agent static archive, two deterministic Debian packages and a standard test IPA. Eight artifact tests pass; repeat builds yield identical hashes for all five manifest artifacts. See IOS_NATIVE_AUDIT.md, IOS_TOOLCHAIN.md and BUILD_IOS.md.
- Established a real pinned paired lockdown TLS session to iPhone 6 (`iPhone7,2`) / iOS 12.5.8. Found Dropbear SSH on USB **44**, authenticated root with the user's provided credential, and transferred only BackThread-owned files using encrypted SSH/SCP. No password/key contents were saved in project reports or source.
- Device root is read only; package manager absent. Standalone Agent and minimal own C probe were killed before main; kernel reports System Policy process-exec denial. Existing ldid re-signing of only the own binary did not change the result. uicache rejected the app's writable staging location. No execution-policy workaround was attempted.
- A later read-only check reported Activated and available Installation Proxy (initially Unactivated/unavailable); no activation operation was performed. Standard paired AFC/app installation was then tested. It reached VerifyingApplication and rejected the ad-hoc signature with ApplicationVerificationFailed / `0xe8008014`. Own installer staging was removed.
- Confirmed removal of the temporary phone transfer archives and smoke executable. Earlier Agent/app copies and a mode-0600 private key remain only under the mode-0700 `/var/mobile/BackThread` directory; no BackThread service/listener is running. These staged copies are not an installed accepted release.
- Preserved Windows Host/simulator product sources. Corrected a test harness bug which turned zero soak seconds into one second. The corrected Release regression has zero warnings/errors, **19/19 regular managed tests plus shared C tests pass**. The initial unintended short-soak failure is recorded; no new long soak is claimed.

## Original checkpoint evidence retained

The Windows modules implement authenticated usbmux/local sockets, WASAPI audio, GSMTC, bounded events, local integration API, resumable storage/worker jobs and tray. The protocol simulator executes real PCM/file/jobs operations. The original checkpoint passed 20 managed checks including a strict 120-second mixed-workload simulator soak: 241 four-MiB transfer/hash cycles, 12,003 PCM packets, zero reported final gaps/drop/overflow/underrun silence after priming. Earlier failed timing runs remain in PERFORMANCE.md and reports.

Actual Windows WASAPI capture/conversion and GSMTC metadata/play/pause/seek were verified at that checkpoint. Self-contained Windows Host/Tray/Simulator packages and API/SSE/archive/checksum/reconnect smoke results remain unchanged. These establish Windows/simulator behavior, not iPhone audio.

| Phase | Current evidence-based state |
|---|---|
| Research/audit | Initial requested repositories reviewed; actual native audit and build-tool provenance added |
| USB/protocol | Real paired TLS/SSH/service checks; application protocol tested in simulation, native device handshake blocked |
| Audio | Windows capture/bridge tested; native AudioQueue compiled/analyzed, no device callbacks or speaker output |
| Media/UI | Windows platform tests; actual ARM64 UIKit build/package, phone launch/touch acceptance blocked |
| Worker | Simulator jobs/files/recovery pass; standalone and embedded native implementations compile but have never executed |
| Handoff | Existing URL boundary tests; native playback remains partial and unverified, no YouTube provider |
| Integration | Windows/simulator preserved, 19 current regressions plus C pass; eight iOS artifact checks and seven analyzer reports clean |
| Release | Windows outputs retained; native build/packages delivered, installed/running iPhone acceptance failed |

No native handshake, PCM playback, UIKit interaction, worker/file operation, app restart/reconnect, process RSS/Jetsam, audio underrun, battery/thermal or physical unplug/listening result is claimed. No firmware, activation setting, boot chain, jailbreak component, system security configuration or unrelated data was modified. No launchd daemon was installed. Resume with NEXT_STEPS.md's first-playback sequence when a normal authorized execution path is available.
