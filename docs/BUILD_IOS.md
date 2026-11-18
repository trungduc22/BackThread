# iOS 12 build and authorized installation

The current sources have actually compiled and linked on Windows as baseline ARM64 / minimum iOS 12.0, with the iPhoneOS 12.4 SDK. Both an embedded-Agent UIKit app package and a standalone Agent package were produced. Ad-hoc signatures pass local integrity checks. **Installation/execution on the attached phone failed; no speaker output is verified.** See [hardware results](HARDWARE_TEST_RESULTS.md) and [native audit](IOS_NATIVE_AUDIT.md).

## Recorded Windows cross-build

Supply these exact external tools (provenance and hashes in [IOS_TOOLCHAIN.md](IOS_TOOLCHAIN.md)); they are not vendored in Git or deliverables. Run from the repository root:

```powershell
$env:SOURCE_DATE_EPOCH='0'
python scripts/build-ios.py `
 --zig artifacts/ios-toolchain/zig/ziglang/zig.exe `
 --sdk artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk `
 --codesign artifacts/ios-toolchain/codesign/apple-codesign-0.29.0-x86_64-pc-windows-msvc/rcodesign.exe
python scripts/analyze-ios.py `
 --zig artifacts/ios-toolchain/zig/ziglang/zig.exe `
 --sdk artifacts/ios-toolchain/sdk/iPhoneOS12.4.sdk
python tests/ios_build_test.py
python scripts/install-ios.py  # prepares an IPA only; no device writes
```

Python 3.15 was used; build helpers use only its standard library. The builder requires Zig 0.13.0. Output stays under `artifacts/ios-build`; only its checked staging directory is recreated. It compiles all seven translation units with `-Wall -Wextra -Werror -Werror=unguarded-availability-new`, uses ARC/blocks for Objective-C, links actual SDK stubs, checks the Mach-O target, signs without a timestamp or external user profile, verifies code/resource hashes, and packages deterministically. The image's SDK load-command field is emitted as 12.0 by this linker; actual headers/stubs are the recorded 12.4 SDK. Deployment minimum is 12.0 in both Mach-O and Info.plist.

Zig 0.13 cannot consume some multi-document SDK TBD reexports directly. The helper splits existing documents by their declared install names, retaining public umbrella reexport declarations, without adding exports or copying frameworks. `tbd-document-manifest.json` records each selected source document and its SHA-256. Final linked libraries are public Apple frameworks and system libraries. No Apple SDK code/framework binary is bundled.

Outputs:

| Artifact | Contents / installation scope |
|---|---|
| `stage/BackThread.app` | UIKit app with embedded Agent, PCM ring/AudioQueue and worker |
| `BackThread.Agent` | Manually started Agent executable using the same implementation; no UIKit |
| `libBackThread.Agent.a` | Static Agent/receiver/shared-wire archive for linking |
| `app.backthread.companion_0.1.1_iphoneos-arm.deb` | Only `/Applications/BackThread.app/*`; app-scoped uicache registration/removal scripts |
| `app.backthread.agent_0.1.1_iphoneos-arm.deb` | Only `/var/mobile/BackThread/bin/BackThread.Agent`; no service, key or launchd entry |
| `BackThread.ipa` | Standard `Payload/BackThread.app` ZIP prepared by install-ios.py; current ad-hoc build is rejected by Apple's installer |
| `build-manifest.json` | Architecture, deployment target, linked libraries and all executable/archive/Debian hashes; runtime/install/speaker flags remain false |

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

## Installation boundary

Use only the user's existing authorized app/package installer. The attached device's root filesystem is read only and lacks dpkg; the Debian packages cannot currently be installed there through that route. Standard paired AFC/Installation Proxy was also tested and rejected the ad-hoc app signature at verification (`0xe8008014`). Local signature hashes do not establish Apple signing trust. A compatible authorized execution path or a valid Apple signing identity/profile is required before retrying. Do not remount system filesystems or change trust/security/jailbreak components.

`python scripts/install-ios.py --install` explicitly requests a standard installer attempt for the built BackThread bundle on the one paired USB device. It validates bundle identity and local integrity, pins the paired device certificate, uploads only a uniquely named BackThread IPA to PublicStaging, sends a normal Install request, records status/error replies and removes that staging file. It does not pair, activate, disable verification or change other apps. The observed attempt failed as recorded; repeating it unchanged is unnecessary.

## Key provisioning and launch, once installation is authorized

For UIKit, launch BackThread, tap Pair, privately provision the Host-generated 64-character hex session key, save and reopen. The key stays in the app's `Library/BackThread/session.key` with mode 0600 and file protection until first unlock. Jobs/files stay in its `Documents/BackThread`. No listener starts without a valid key.

The standalone tool accepts only:

```text
BackThread.Agent --key-file /var/mobile/BackThread/session.key --state /var/mobile/BackThread/state
```

This is a manual foreground process, with SIGINT/SIGTERM shutdown and root-to-mobile privilege drop before Foundation initialization. It requires mobile-readable key and mobile-writable app-owned state. **Its execution is currently denied on this phone.** No automatic daemon or persistence is installed. UIKit and standalone background scheduling/audio privileges are unverified until they actually run.

The Host uses `transport: "usb"` to reach iPhone loopback port 23451 through Apple Mobile Device Service. No extra BackThread port forward, Wi-Fi, cloud service or iPhone internet is required for PCM and jobs. SSH is separate and currently uses device port 44. `scripts/usb-stdio.py --device-port 44` can be used as an OpenSSH ProxyCommand with a verified isolated known-host record. `scripts/usb-port-forward.py` is an alternative temporary loopback-only forward with at most four active tunnels and a bounded lifetime. Neither is a phone service installer.

Follow [NEXT_STEPS.md](NEXT_STEPS.md) for real handshake, first PCM/speaker confirmation, UIKit/worker, memory and reconnection acceptance. Compile/package success must remain separate from those results.
