# Native foundation checkpoint · 2026-11-19

## Baseline and recoverability

- BackThread companion branch and tested baseline: `57ff8d68933050b4eaaed47ae7eef2ece6e4edd9`. Remote master was `9c5fdac`, with merge base `6b986ce`. The dedicated worktree `outputs/BackThread-Foundation` started clean from the companion branch.
- The original companion worktree still contains the unfinished BuildRunner, Host config/API wiring, native automation runner and their tests. These edits were excluded from the baseline and preserved in place.
- NativeShellKit reference head: `89147f1058f853eb423624d468780aab1e064e65`. Local compatibility branch `codex/backthread-compat` at `ff033dc25ddf9aa05c66da7170ca663b3ee373ea`.
- BackThread implementation commits: `2e5ea97` (transport), `ee7da7c` (Core, redaction, diagnostics and playback), `4e09cb7` (Windows CI). The current foundation branch remains based on the original companion tree.

## Architecture and implementation

- The .NET 8 Host executable now composes `BackThread.Host.Core`. Tray and managed tests reference Core directly. Core orchestrates the existing Audio, Media, Worker, Events and Transport projects. Simulator and DeviceTests keep their original component dependencies. The Host dependency manifest includes Core and has no WinUI, Windows App SDK or Desktop framework dependency. The Windows 10 1809 supported API floor is unchanged; validation ran on the available Windows machine.
- NativeShellKit Abstractions, Hosting, Security, Communication, Diagnostics, Windows and Tray compiled for .NET 8 and .NET 10. Abstractions does have a NuGet dependency on MEDI Abstractions 10.0.0 due to the SDK's DI bridge. The SDK projects keep their Windows build 19041 floor. BackThread adopts only the dependency-free SecretRedactor source with its exact upstream commit, file digest and MIT license. This avoids imposing SDK OS/dependency floors on the Host. Redaction is limited to recognized formats in Host diagnostics and is not a credential detection guarantee.
- NativeShellKit's pipe client previously accepted unverified responses and retried after request transmission. The compatibility branch uses signed protocol 2.0 replies bound to AppId, request ID and request nonce, a current-user pipe, and no automatic request replay. Existing SDK pipe names and DPAPI locations are retained. SDK endpoints require coordinated upgrade. BackThread does not enable this IPC or change any existing USB/Wi-Fi keys, routes or wire version.
- BackThread now correlates replies by ID and channel, ignores late replies instead of dispatching them as commands, and strictly limits outstanding requests to 64. A test caught a regression on immediate EOF after a valid reply. Pending completion now follows the reader's decoded result or terminal failure, rather than racing the connection cancellation token.
- The simulator serializes playback reset/stop against render ticks, reports skipped playback ticks separately, and discards complete stale PCM after stop while preserving the control/worker session. Managed tests cover hostile ordering and the normal stop/start path. No PCM format or queue capacity changed.
- Wi-Fi status reports stages and counts for TCP accept, TLS, enrollment ID and HMAC. It reports failure stage and exception type without private material. Local tests cover authenticated handover and bad pin, session and key. The prior real-device timeout still lacks enough data to distinguish the network path, firewall, local interface or client-side TLS pin.
- The iOS remote native handoff restores PC ownership if initial playback fails or misses the deadline. Existing UIKit layout and drawer remain intact. Dual playback, Build/Run Deck, Context Dock, app-launch idempotency and scheduled account actions are pending stability gates.

## Commands and measured scope

Use .NET SDK 8.0.428 for BackThread and .NET SDK 10.0.404 for the NativeShellKit compatibility build. Tests used the installed .NET 8 and .NET 10 runtimes separately.

| Check | Command | Result |
| --- | --- | --- |
| Baseline solution | `dotnet build BackThread.sln -c Release` | Exit 0, 0 warnings/errors |
| Baseline functional | `pwsh -NoProfile -File scripts/test.ps1` | Exit 0, 35/35 managed and three C suites |
| Baseline strict audio | `pwsh -NoProfile -File scripts/test.ps1 -SoakSeconds 120` | Exit 1, 35/36, audio gate failed |
| Foundation solution and functional | Same build and functional commands on `ee7da7c` | Exit 0, 37/37 managed and three C suites |
| Foundation strict audio | `pwsh -NoProfile -File scripts/test.ps1 -SoakSeconds 120` | Exit 1, 120.78 s, 147 worker jobs |
| SDK dual-target | `dotnet build src/NativeShellKit.<project> -c Release` for Tray, Hosting, Diagnostics and Communication (includes dependencies) | Exit 0 for both targets |
| Scoped SDK verification | `dotnet test tests/NativeShellKit.Foundation.Tests -c Release -f net8.0-windows10.0.19041.0` and the same command with `net10.0-windows10.0.19041.0` | 5/5 on each runtime, plus the existing .NET 10 pipe test passed |
| Python regression | `python -m unittest discover -s tests -p <pattern>` for `ios_analyzer_test.py`, `ios_device_tool_test.py` and `phone_control_test.py` | 22/22 |
| Native compile and static audit | `scripts/build-ios.py` / `scripts/analyze-ios.py` with Zig 0.13.0, LLD 23.1.5, SDK 12.4 and rcodesign 0.29.0 | ARM64 iOS 12.0 compile/sign checks, 19 analysis units clean |
| Artifact verification | `python -m unittest discover -s tests -p ios_build_test.py` against the freshly built artifacts | 9/9 |
| Dependency audit | `dotnet list BackThread.sln package --vulnerable --include-transitive --format json` | Exit 0, zero listed advisories at test time |
| IPA packaging | `python scripts/install-ios.py --build-dir artifacts/ios-build` | File only, SHA-256 `8f1f743e191831fc5d96a239273d590dc77765d90f4a324dbeff608907d49785` |

Initial SDK test invocation used the wrong runtime search path and disabled patch roll-forward. Both runtimes then passed using their separate installations. The transient compiler mismatch during SDK source editing was resolved by rebuilding the complete target graph. Managed output is from BackThread's executable runner.

The strict gate still fails. The baseline reported 32,640 receiver-drop bytes and 51,840 zero-fill bytes with no sender queue drops. The final run reported 3,840 receiver-drop bytes, 26,880 zero-fill bytes, 0 sender queue drops, 0 sequence gaps and 0 skipped render ticks. Its maximum arrival gap was 114.58 ms. The intermediate instrumented run also failed. Workloads and host scheduling varied, so these runs do not establish a performance improvement or a single root cause.

The machine-readable soak measurements are in [foundation-soak.json](reports/foundation-soak.json). Simulator CPU/RSS are from the common sender/receiver/test process. A short observation of the installed Tray recorded 153,862,144 bytes working set, 46,665,728 private bytes and 0.1875 CPU seconds over ~5.02 seconds. Tray workload was uncontrolled. There is no comparable Host/Tray idle-vs-streaming profile, device thermal measurement, acoustic latency or new listening confirmation.

## Safe acceptance order

1. Check out the foundation branch in a separate worktree. Run `pwsh -NoProfile -File scripts/test.ps1` with .NET 8. This creates its own loopback test sessions and keys. For interactive simulator use, follow [Run with the simulator](../README.md#run-with-the-simulator) with a worktree-local configuration and unused ports.
2. Reproduce the strict simulator soak without concurrent builds. Correlate sender lateness and queue delay with receiver gap, overflow, zero fill and skipped render ticks. Gate audio changes on controlled runs before and after. Add an ordered fault case for PCM across an ownership switch and measure real restart latency separately.
3. For future authorized device testing, match `artifacts/ios-build/BackThread.ipa` to the checksum above and refer to [iOS build and verification](BUILD_IOS.md). Use the approved installation path for BackThread only after the user authorizes it. Start with native handoff failure/timeout and returning PC ownership, then a short controlled USB listening and worker pass. Hold native deployment until the user asks to install. No system repairs or key changes are needed for the offline checks.
4. For future authorized Wi-Fi testing, first verify USB enrollment and selected private interface. Inspect `/v1/wifi` while active: zero TCP accepts leaves reachability/interface unresolved; a TLS failure precedes enrollment validation; enrollment and HMAC counters isolate the subsequent stages. Confirm actual transport switch separately and revoke on exit.
5. Complete the Build/Run Deck and optional workbench modules on top of a stable audio/control baseline. Existing unfinished prototypes remain in the original worktree.

Hardware acceptance for this checkpoint is blocked by the current deployment boundary. No test on the iPhone, installed Host update, firmware change, trust change or device-security change was performed.
