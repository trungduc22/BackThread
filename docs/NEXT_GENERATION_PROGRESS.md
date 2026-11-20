# BackThread Next Generation — Progress Checkpoint

Durable resumption checkpoint across the 5-repository ecosystem maintenance and BackThread Next Generation roadmap.

## Completed Milestones

### Milestone A — Ecosystem PR Reconciliation (DONE)
- **NativeShellKit** `main` (`0c9efe4`): `0.2.0-alpha.4` published to GitHub Packages (13 `.nupkg`, run `44726417367`).
- **GhostCode** PR #1 (`eco/ghostcode-m2-m4` -> `main` `cc02c9b`): Merged with green CI.
- **HiberDesk** PR #2 (`eco/hiberdesk-m2-m4` -> `main` `a7a26fd`): Merged with green CI; PR #1 (`codex/fix-build-and-verified-smoke-test`) auto-marked merged.
- **BackThread** PR #1 (`eco/backthread-m2-m5` -> `master` `cbfcc7d`): Merged with green 3-OS CI.
- **ed-local-ai** PR #1 (`eco/ed-local-ai-m2-m6` -> `main` `0dadb64`): Merged with green CI (235/235 backend tests).

### Milestone B — Safe GitHub Branch Cleanup (DONE)
- **Deleted (11 strict-ancestor `SAFE_DELETE` branches):**
  - `NativeShellKit` (6): `convergence/sdk-2.0` (`0c652d7`), `docs/architecture-2.0` (`76f57ea`), `eco/nativeshellkit-m1` (`7bf956e`), `feature/ecosystem-integration-contracts` (`7c3e34c`), `fix/sdk2-worker-drain` (`fcd90ec`), `migration/m12-sdk` (`f8f68bf`).
  - `HiberDesk` (3): `codex/fix-build-and-verified-smoke-test` (`9bee11e`), `migration/nativeshellkit-hiberdesk` (`495e027`), `convergence/hiberdesk-2.0` (`7444cbc`).
  - `GhostCode` (2): `migration/nativeshellkit-ghostcode` (`fa44d1a`), `convergence/ghostcode-2.0` (`9c3da3f`).
- **Retained (7 non-ancestor `KEEP` / `REVIEW_REQUIRED` branches):**
  - `HiberDesk`: `integration/backthread-ghostcode-hub` (`d196a9f`, `REVIEW_REQUIRED`).
  - `GhostCode`: `integration/hiberdesk-events` (`a19742e`, `REVIEW_REQUIRED`).
  - `BackThread`: `codex/companion-workbench` (`57ff8d6`, `KEEP`), `codex/native-foundation` (`4710def`, `KEEP`), `codex/phonebox-companion` (`2bb5582`, `KEEP`), `integration/hiberdesk-bridge` (`5a29fcf`, `REVIEW_REQUIRED`).
  - `ed-local-ai`: `migration/nativeshellkit-ed-local-ai` (`367cb22`, `KEEP`).

### Milestones C & D — Wi-Fi-First Architecture, Per-Device Credentials & Dual iOS 12/15 Profiles (DONE — PR #2)
- **Branch / PR:** `feat/backthread-wifi-first-and-ios-profiles` (BackThread PR #2).
- **CI Verified (`44733039364` — ALL GREEN):**
  - `linux-core`: **PASS** (50/50 tests, Daemon smoke test, C11 ASan/UBSan + Clang, Python iOS unit tests).
  - `windows-desktop`: **PASS** (`BackThread.Desktop.sln` Daemon + WinUI 3 App, all Windows tests, Daemon smoke test).
  - `macos-ios`: **PASS** (BOTH `ios12-universal` `BackThread-iOS12.ipa` + `BackThread-iOS12-rootful.deb` AND `ios15` `BackThread-iOS15.ipa` + `BackThread-iOS15-rootless.deb` compiled & Mach-O validated).

### Milestones G, F, H, E & P4 Audit — Touchpad/Drawing Pad, Legacy Web TLS Client, Display Adaptation, PhoneBox Adapter & Audit (IN PROGRESS — PR #3)
- **Branch:** `feat/backthread-input-legacy-web-display-phonebox`.
- **Files added/modified:**
  - Milestone G: `src/Windows/Host/RemoteInputEngine.cs`, `HostConfig.cs`, `HostRuntime.cs`, `LocalApi.cs`, `DaemonContract.cs`, `DaemonIpcHost.cs`.
  - Milestone F: `src/Windows/Host/LegacyWebServer.cs` (TLS-only, strict ES5 JS `<15 KB`, WebSocket + XHR fallback, mandatory per-device auth).
  - Milestone H: `src/Windows/Transport/DisplayProfileCatalog.cs`, `src/iOS/UI/BTDisplayProfile.h/.m`, `src/iOS/Agent/BTAgent.m`.
  - Milestone E: `src/Windows/Transport/PhoneBoxAdapter.cs`, `src/iOS/Transport/BTPhoneBoxClient.h/.m`, `THIRD_PARTY_NOTICES.md`.
  - Build/Tests/Audit: `.github/workflows/ci.yml`, `scripts/build-ios.py`, `src/iOS/Makefile`, `tests/BackThread.Tests/NextGenFeaturesTests.cs`, `tests/BackThread.Tests/Program.cs`, `docs/NEXT_GENERATION_IDEAS.md`.
- **Verification:** Local Python unit tests + Node.js ES5 syntax verification; full 3-OS GitHub Actions CI pending PR #3 push.

## Remaining Limitations & Hardware Tests Pending
- Physical iPhone 6 (iOS 12.5.8), physical iOS 15+ device, physical legacy iOS 8–11 Safari / Android WebView / Windows Phone browser, and physical Win32 cursor/drawing feel tests remain `NOT YET PHYSICALLY VERIFIED` (CI verifies compilation, Mach-O headers, archive structure, protocol/TLS/WebSocket/input-engine math, and micro-benchmarks).
