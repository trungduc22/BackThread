# BackThread PhoneBox continuation

The Antigravity conversation **Phonebox Feature Analysis And Integration** and its **Backthread Upgrade Analysis** artifact were reviewed on 2026-11-19. The report names BackThread `9c5fdac`, NativeShellKit `0.1.0-alpha.1`, and PhoneBox `0.9.21` as its analysis baselines. The user approved pursuing P0 through P2.

## Application boundaries

| Application | Responsibility |
|---|---|
| BackThread | Owns the iPhone app, device sessions, media, audio, telemetry, and offloading. Antigravity started a headless Windows `BackThread.Daemon` for the same product. A separate BackThread WinUI settings window can start and exit independently. |
| HiberDesk | Owns Windows workspaces and its own NativeShellKit shell. Its optional BackThread controls are clients of the BackThread-owned, same-user bridge. The existing bridge branch is `integration/hiberdesk-bridge`. |
| PhoneBox | Upstream reference for the selected features below. A community iOS client for PhoneBox.Agent is an independent proposal with its own pairing protocol and upstream review. [PhoneBox repository](https://github.com/ersinkoc/phonebox) |

Use NativeShellKit packages at an explicit version when wiring BackThread's shell. Keep window creation and WinUI out of the daemon. Treat daemon-to-BackThread-window IPC and the opt-in HiberDesk API as separate contracts. Preserve existing device provisioning, USB PCM, and standalone Host entry points through the migration.

## Recovered scope

| Priority | Work described in the report | First verification |
|---|---|---|
| P0 | TLS certificate pinning over Wi-Fi and Tailscale with interface selection, QR one-time enrollment and USB provisioning, USB-first fallback and multi-PC pairing, demand-driven sampling | Claim expiry and replay, pin mismatch, revocation, USB ownership and reconnect, sampler idle cost, Wi-Fi changeover on real hardware |
| P1 | Per-app audio, GPU telemetry, rules with hysteresis and auto-undo, Windows Job Objects for owned child processes | Unsupported devices, repeatable rule transitions, cleanup after cancellation and host exit, existing audio regression |
| P2 | Charge Guard, developer/session monitoring, and a constrained feed proxy | Report charging capabilities accurately, verify event freshness and retention, deny private/loopback/redirected feed destinations where policy requires, test each provider independently |

Antigravity grouped the work into M1 (.NET 10 and connectivity), M2 (iOS 15 alongside iOS 12), M3 (lower idle load and offloading), and M4 (Windows daemon/UI). Feature completion requires source verification and platform-specific tests. Keep the existing native UIKit layout and treat iPhone 6 on iOS 12.5.8 as its own acceptance target.

## Source recovery and verification

The recovered Antigravity command log records two attempted commit operations, for the Windows core upgrade and iOS upgrade. Its last turn lists 11 uncommitted files for the daemon and related build wiring, with 1,203 inserted lines. Neither remote commit SHA nor a successful push appears in the recovered log. The GitHub branches checked were `master` at `9c5fdac`, `codex/native-foundation` at `4710def`, `codex/companion-workbench` at `57ff8d6`, and `integration/hiberdesk-bridge` at `5a29fcf`. The remote source must be recovered before the logged changes are treated as available in this checkout.

Once available, import the Antigravity revisions into an isolated branch and establish their exact merge base. Compare the divergent Foundation and bridge changes selectively, especially the PCM/job journal isolation and HiberDesk's same-user capability boundary. Build the original imported revision before making changes. Then check package versions and the daemon dependency manifest, run the Windows regression and simulator, and build both iOS profiles before any installation.

The independent local reference at `4710def` passed 38 managed tests and the shared C protocol/PCM, navigation, and palette suites on Windows. This run covered the existing TLS pin and HMAC tests and the simulator journal isolation, with no new hardware results. The prior strict simulator soak remains open. See [Audio follow-up](AUDIO_JOURNAL_FOLLOWUP.md).
