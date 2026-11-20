# BackThread Next Generation — Progress Checkpoint

Durable resumption checkpoint across the 5-repository ecosystem maintenance and BackThread Next Generation roadmap.

## Completed Milestones

### Milestone A — Ecosystem PR Reconciliation (DONE)
- **NativeShellKit** `main` (`0c9efe4`): `0.2.0-alpha.4` published to GitHub Packages (13 `.nupkg`, run `44726417367`).
- **GhostCode** PR #1 (`eco/ghostcode-m2-m4` -> `main` `cc02c9b`): Merged after green `SDK Compatibility Gate` + `NativeShellKit migration checks`.
- **HiberDesk** PR #2 (`eco/hiberdesk-m2-m4` -> `main` `a7a26fd`): Merged after green `HiberDesk CI` + `SDK Compatibility Gate`; PR #1 (`codex/fix-build-and-verified-smoke-test`) automatically marked merged as ancestor.
- **BackThread** PR #1 (`eco/backthread-m2-m5` -> `master` `cbfcc7d`): Merged after green 3-OS `BackThread CI` + `SDK Compatibility Gate`.
- **ed-local-ai** PR #1 (`eco/ed-local-ai-m2-m6` -> `main` `0dadb64`): Merged after green `SDK Compatibility Gate` (`winui3` + `practice-fast`, 235/235 backend tests).

### Milestone B — Safe GitHub Branch Cleanup (DONE)
- **Deleted (11 strict-ancestor `SAFE_DELETE` branches):**
  - `NativeShellKit`: `convergence/sdk-2.0` (`0c652d7`), `docs/architecture-2.0` (`76f57ea`), `eco/nativeshellkit-m1` (`7bf956e`), `feature/ecosystem-integration-contracts` (`7c3e34c`), `fix/sdk2-worker-drain` (`fcd90ec`), `migration/m12-sdk` (`f8f68bf`).
  - `HiberDesk`: `codex/fix-build-and-verified-smoke-test` (`9bee11e`), `migration/nativeshellkit-hiberdesk` (`495e027`), `convergence/hiberdesk-2.0` (`7444cbc`).
  - `GhostCode`: `migration/nativeshellkit-ghostcode` (`fa44d1a`), `convergence/ghostcode-2.0` (`9c3da3f`).
- **Retained (7 non-ancestor `KEEP` / `REVIEW_REQUIRED` branches with unique commits/docs):**
  - `HiberDesk`: `integration/backthread-ghostcode-hub` (`d196a9f`, `REVIEW_REQUIRED` — contains `docs/ECOSYSTEM_VERIFICATION.md`).
  - `GhostCode`: `integration/hiberdesk-events` (`a19742e`, `REVIEW_REQUIRED` — non-ancestor commit).
  - `BackThread`: `codex/companion-workbench` (`57ff8d6`, `KEEP`), `codex/native-foundation` (`4710def`, `KEEP`), `codex/phonebox-companion` (`2bb5582`, `KEEP`), `integration/hiberdesk-bridge` (`5a29fcf`, `REVIEW_REQUIRED` — contains `HostInstanceLease.cs` & `docs/HIBERDESK_BRIDGE.md`).
  - `ed-local-ai`: `migration/nativeshellkit-ed-local-ai` (`367cb22`, `KEEP` — contains `M12_MIGRATION.md`).

### Milestones C & D — Wi-Fi-First Architecture, Per-Device Keys & Dual iOS 12/15 Profiles (IN PROGRESS — Branch `feat/backthread-wifi-first-and-ios-profiles`)
- **Files modified:** `DeviceRegistry.cs`, `LanTlsServer.cs`, `QrPairingService.cs`, `HostConfig.cs`, `HostRuntime.cs`, `LocalApi.cs`, `BTWifiClient.h`, `BTWifiClient.m`, `BTAgent.h`, `BTAgent.m`, `.github/workflows/ci.yml`, `docs/IOS_BUILD_PROFILES.md`, `tests/BackThread.Tests/Program.cs`.
- **Verification:** Pending 3-OS GitHub Actions CI (`linux-core`, `windows-desktop`, dual-profile `macos-ios` for BOTH iOS 12 and iOS 15).

## Next Concrete Actions
1. Verify Milestones C & D 3-OS CI (including separate iOS 12 + iOS 15 Mach-O & rootful/rootless `.deb` builds).
2. Implement Milestone G (Precision Touchpad + Drawing Pad + safety/telemetry) & Milestone F (Lightweight ES5 Legacy Web Client over TLS).
3. Implement Milestone H (Display Adaptation + OLED guard) & Milestone E (Optional PhoneBox Compatibility Adapter).
4. Complete P4 independent architecture audit (`docs/NEXT_GENERATION_IDEAS.md`).
