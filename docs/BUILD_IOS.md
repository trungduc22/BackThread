# iOS 12 build and authorized installation

The current sources have actually compiled and linked on Windows as baseline ARM64 / minimum iOS 12.0, with the iPhoneOS 12.4 SDK. Both an embedded-Agent UIKit app package and a standalone Agent package were produced. Ad-hoc signatures pass local integrity checks. **Embedded Agent execution was verified in the recorded prepared environment; earlier two listening checks failed; the latest repaired app was upgraded and a new fixture was heard. Ordinary Windows audio was also heard with stutter. Latest Windows batching remains physically untested after unplug.** See [hardware results](HARDWARE_TEST_RESULTS.md) and [native audit](IOS_NATIVE_AUDIT.md).

## Recorded Windows cross-build

Supply these exact external tools (provenance and hashes in [IOS_TOOLCHAIN.md](IOS_TOOLCHAIN.md)); they are not vendored in Git or deliverables. Run from the repository root:

```powershell
$env:SOURCE_DATE_EPOCH='0'
# Supply the existing LLD 23.1.5 executable. This session found it in Rust's toolchain:
$lld='C:/Users/Duc/.rustup/toolchains/stable-x86_64-pc-windows-gnu/lib/rustlib/x86_64-pc-windows-gnu/bin/rust-lld.exe'
python scripts/build-ios.py `
 --linker $lld `
 --zig artifacts/ios-toolchain/zig/ziglang/zig.exe `
 --sdk artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk `
 --codesign artifacts/ios-toolchain/codesign/apple-codesign-0.29.0-x86_64-pc-windows-msvc/rcodesign.exe
python scripts/analyze-ios.py `
 --zig artifacts/ios-toolchain/zig/ziglang/zig.exe `
 --sdk artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk
python tests/ios_build_test.py
python scripts/install-ios.py  # prepares an IPA only; no device writes
```

Python 3.15 was used; build helpers use only its standard library. The builder requires Zig 0.13.0 and explicit LLD 23.1.5. The manifest records compiler/linker/signer executable hashes. If a Windows Python app alias exits unexpectedly, invoke the actual interpreter executable directly; this session validated Python 3.15.2 that way. No system Python settings need changing. Output stays under `artifacts/ios-build`; only its checked staging directory is recreated. It compiles all seven translation units with `-Wall -Wextra -Werror -Werror=unguarded-availability-new`, uses ARC/blocks for Objective-C, links actual SDK stubs, checks the Mach-O target, signs without a timestamp or external user profile, verifies code/resource hashes, and packages deterministically. The LLD image records minimum iOS 12.0 and SDK 12.4; actual headers/stubs are the recorded 12.4 SDK. Deployment minimum is 12.0 in both Mach-O and Info.plist.

Zig supplies Clang and ar; LLD performs Mach-O linking against the **original multi-document TBDs**, so umbrella reexports remain intact. The earlier normalized-TBD Zig link path is historical and is no longer the default. The successful LLD path is proven by device execution, but no controlled claim attributes every earlier exit to Zig: normal URL launching also involved an invisible iOS confirmation. LLD uses `-platform_version ios 12.0 12.4 -no_fixup_chains` and legacy dyld metadata. Final linked libraries are public Apple frameworks/system libraries. No Apple SDK/framework binary is bundled.

Outputs:

| Artifact | Contents / installation scope |
|---|---|
| `stage/BackThread.app` | UIKit app with embedded Agent, PCM ring/AudioQueue and worker |
| `BackThread.Agent` | Manually started Agent executable using the same implementation; no UIKit |
| `libBackThread.Agent.a` | Static Agent/receiver/shared-wire archive for linking |
| `app.backthread.companion_0.1.2_iphoneos-arm.deb` | Only `/Applications/BackThread.app/*`; app-scoped uicache registration/removal scripts |
| `app.backthread.agent_0.1.2_iphoneos-arm.deb` | Only `/var/mobile/BackThread/bin/BackThread.Agent`; no service, key or launchd entry |
| `BackThread.ipa` | Standard `Payload/BackThread.app` ZIP prepared by install-ios.py; current UI installs in the user-prepared AppSync environment; native repair app executed and fixture heard; quality not certified |
| `build-manifest.json` | Architecture, deployment target, linked libraries and all executable/archive/Debian hashes; build-only runtime/install/speaker flags remain false; physical evidence is separate |

The `.deb` architecture label `iphoneos-arm` follows rootful iOS package convention; the contained Mach-O code is **ARM64**. Neither package contains an SSH credential, pairing record, session key or provisioning identity. The UI embeds an Agent, so do not run it together with the standalone Agent on the same port.

Repeat the build with the same inputs and `SOURCE_DATE_EPOCH` and compare `artifacts` in the two manifests. Two consecutive builds were byte-identical for all five recorded artifacts. This is a toolchain-specific reproducibility check, not a runtime test.

## Existing Theos/macOS route (not executed here)

The checked-in Makefiles also target rootful ARM64 / minimum iOS 12.0. An existing compatible Theos setup, supplied SDK and signing configuration are required. No Theos or Xcode installation was available in this session, so this route is unverified.

```bash
export THEOS=/path/to/existing/theos
unset THEOS_PACKAGE_SCHEME
bash scripts/build-ios.sh
```

The script builds UI and standalone Agent separately using `make clean package FINALPACKAGE=1`. Theos packages appear under each project's package directory. The script performs no install or launch-service changes. The shared launch PNG is a tracked resource, so both build routes include it.

## Installation and data preservation (0.1.2)

Before the user's device pause, existing AppSync 116.2/dpkg and the normal installer supported installation/Upgrade, and the full app/embedded Agent ran. Standalone execution was separately denied. The current environment is unknown after the user's maintenance. Recheck only after readiness; do not install or repair any missing system component. Earlier signature rejection and failed-entry reports are historical; see HARDWARE_TEST_RESULTS.md.

`python scripts/install-ios.py --install` validates the built UI identity/ARM64/legacy dyld metadata and local signature hashes, pins the paired device certificate, stages a uniquely named own IPA, selects Install for a new app or Upgrade for an existing one, records status/error replies, then removes its staging file. A physical sentinel test verified Upgrade data preservation despite a rotated container path. Refresh `python scripts/ios-app-info.py --component ui` after an update; private container/path metadata is stored only in ignored artifacts/ios-private.

The Debian writer now includes explicit app-owned directory headers required by dpkg. It excludes shared /Applications, /var and /var/mobile directory metadata. Agent's BackThread directory uses target mobile UID/GID 501 and mode 0700; bin is 0755. The original 0.1.1 Debian archives lacked these headers and are superseded. Artifact checks verify required parent headers, owner/mode and payload scope. A corrected temporary probe package was actually installed/removed with dpkg; full UI/Agent Debian runtime is not inferred.

Probe build uses the same compiler/SDK/signer arguments as the core:

```powershell
python scripts/build-ios-probe.py --linker <LLD23.1.5.exe> --zig <zig.exe> --sdk <iPhoneOS12.4.sdk> --codesign <rcodesign.exe>
# Add --c-entry to isolate libc entry from UIKit/Foundation startup.
python scripts/install-ios.py --component probe --install
```

Default probe is UIKit; C entry is a distinct diagnostic build. Both are temporary BackThread components with a launch-only backthread-probe URL. Normal `uiopen backthread-probe://launch` requests launch; uiopen returning zero or `uicache -l` displaying metadata is not entry proof. Tie any marker to the current launch rather than an old JSON file. LLD C-entry and UIKit probes obtained fresh execution proofs; the full embedded Agent subsequently served real protocol requests. `--launch-ui` is an optional temporary probe that asks the existing normal launch service to open only BackThread's fixed bundle identifier, without force-unlock or security options. Do not install probes while device work is paused.

### Scoped removal

For an IPA installed through Installation Proxy, `python scripts/install-ios.py --component ui --uninstall` removes only app.backthread.companion and its own container, including its app data/key. For the temporary probe, select `--component probe`. Fixed identifiers prevent querying or modifying unrelated apps.

For a rootful package, use the prepared environment's normal `dpkg --remove app.backthread.companion`, `app.backthread.agent`, or `app.backthread.probe` as appropriate. Maintainer scripts reference only the corresponding app bundle. Never use a recursive delete on /Applications or /var/mobile. App-owned data not packaged with the Agent is deliberately retained; remove specific own test fixtures only when intended. Earlier removal tests operated only on the named BackThread probe. Later diagnostics installed another own probe; current presence is unknown after device maintenance. Never assume a historical removal result describes the current phone.

## Key provisioning and launch, once installation is authorized

For UIKit, launch BackThread, tap Pair, privately provision the Host-generated 64-character hex session key, save and reopen. The key stays in the app's `Library/BackThread/session.key` with mode 0600 and file protection until first unlock. Jobs/files stay in its `Documents/BackThread`. No listener starts without a valid key.

The standalone tool accepts only:

```text
BackThread.Agent --key-file /var/mobile/BackThread/session.key --state /var/mobile/BackThread/state
```

This is a manual foreground process, with SIGINT/SIGTERM shutdown and root-to-mobile privilege drop before Foundation initialization. It requires mobile-readable key and mobile-writable app-owned state. **Standalone execution remains explicitly denied on this phone.** No automatic daemon or persistence is installed. Embedded UIKit execution is verified historically; background scheduling/route/interruption behavior and standalone execution remain unverified/blocked.

The Host uses `transport: "usb"` to reach iPhone loopback port 23451 through Apple Mobile Device Service. No extra BackThread port forward, Wi-Fi, cloud service or iPhone internet is required for PCM and jobs. SSH is separate and currently uses device port 44. `scripts/usb-stdio.py --device-port 44` can be used as an OpenSSH ProxyCommand with a verified isolated known-host record. `scripts/usb-port-forward.py` is an alternative temporary loopback-only forward with at most four active tunnels and a bounded lifetime. Neither is a phone service installer.

Follow [NEXT_STEPS.md](NEXT_STEPS.md) for real handshake, first PCM/speaker confirmation, UIKit/worker, memory and reconnection acceptance. Compile/package success must remain separate from those results.

## Current repair validation scope

`analyze-ios.py` writes a structured `results.json` with a fresh run ID, expected source count, `complete` and `passed`. An interrupted run is not clean even if old per-source files remain. Ten translation units include C entry, loader probe (generated from the actual app image) and the launch-own-UI macro. Tooling unit tests are `python tests/ios_analyzer_test.py`. Inspect the complete current report and command exit status together.

For a reproducibility check, build into two separate repository artifact subdirectories with identical inputs and compare all five artifact hashes. Only the same-input comparison reported in the current `repairs-reproducibility.json` applies to the repaired LLD build; older Zig-only comparisons are historical. No build command here deploys.
