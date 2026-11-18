# Hardware test results

Evidence date: 2026-11-18, Asia/Saigon. Follow-up from commit `738a497`. **First Windows-to-iPhone PCM playback has not been achieved.** BackThread never reached `main` on the device. Simulator success is not counted as phone success.

## Environment and native build

- Windows 10 x64 build 19045; Apple Mobile Device Service and USB driver are working. usbmux is at `127.0.0.1:27015`.
- No usable macOS/Xcode, Theos installation or WSL distribution was found. Workspace-local .NET 8 SDK 8.0.428 from the earlier checkpoint remains available.
- Acquired workspace-local Zig 0.13.0 / Clang 18.1.6, the Theos-hosted iPhoneOS 12.4 SDK and apple-codesign 0.29.0. See [toolchain provenance](IOS_TOOLCHAIN.md) for exact versions, hashes and SDK terms. No SDK/tool binaries are redistributed in the source or package deliverables.
- All seven native translation units compile with warnings treated as errors, including availability checks. UIKit and standalone Agent link as baseline ARM64, platform iOS, minimum 12.0. The iPhone 6 A8 does not require ARM64e.
- Both executables have ad-hoc SHA-256 code signatures; every CodeDirectory page hash, bundle special-slot hash and sealed resource hash was checked. Eight artifact/package tests pass, including deliberate code/resource corruption rejection. Clang's analyzer reports zero diagnostics on all seven translation units.
- Two consecutive builds produced identical hashes for both executables, the Agent static archive and both `.deb` packages. The packages contain only BackThread-owned paths. This establishes reproducibility with the recorded tool versions, not device trust or runtime correctness.

## Physical device checks actually executed

| Check | Verified result |
|---|---|
| USB enumeration and identity | Real `iPhone7,2` (iPhone 6), iOS `12.5.8` |
| Current pairing/trust | Paired lockdown TLS session succeeded; exact device certificate pinned against the existing pair record |
| ActivationState, read only | Initially `Unactivated`; later `Activated`, observed twice with Installation Proxy available. BackThread performed no activation operation. The cause of the state change was not established. |
| SSH | Port 22 refused; port **44** accepted `SSH-2.0-dropbear_2020.81`; root login succeeded using the user-supplied credential |
| SSH host verification | USB key scan, isolated strict known-host record; RSA fingerprint `SHA256:95akdem771MFjOLvxuy2NFGye5ozSsAdkJux7InDJOo` |
| USB forwarding | Temporary loopback-only forward carried SSH/SCP; a listener-free OpenSSH ProxyCommand also carried final cleanup successfully |
| Other ports | 2222 and BackThread 23451 refused |
| Filesystem / package environment | Root filesystem and `/binpack` read only; `/private/var` writable; no `dpkg`, `su`, `open` or `uiopen` found; existing `ldid`, `uicache`, tar and SCP available |
| Existing UI environment | SpringBoard listed by read-only launchctl inspection; it was not restarted or changed |
| AFC / AFC2 | AFC available; AFC2 returns `InvalidService` |
| Installation Proxy | Initially `InvalidService`; available in later paired sessions; actual app install attempted and rejected as detailed below |
| Syslog | Relay available; bounded collection retained only BackThread matches. No BackThread runtime lines observed. Unrelated app logs were not stored. |

These capabilities do not establish a fully prepared jailbreak environment. Root SSH access does not imply arbitrary native execution or app installation authorization.

## BackThread deployment and failures

Only BackThread files were transferred over authenticated SSH/SCP into `/var/mobile/BackThread`, plus a temporary BackThread archive under `/var/mobile`. The standalone Agent reuses the same receiver/protocol implementation as the UIKit app and drops root to mobile before Foundation use. A fresh private session key was provisioned with mode 0600. Its contents and the SSH password were never written to project source or reports.

Launching the ad-hoc-signed Agent returned `Killed: 9` before a listener appeared. Re-signing that BackThread-owned binary with the device's existing `ldid -S` also returned `Killed: 9`. A tiny, separately signed BackThread C probe which only calls `write()` was likewise killed before `main`. Kernel diagnostics included:

```text
System Policy: deny(1) process-exec* /private/var/mobile/BackThread/bin/BackThread.Agent
process-exec denied while updating label
```

These are execution-policy denials, not evidence of an application crash, audio bug or measured memory failure. The deeper policy configuration was not changed or bypassed. Registering the staged app with the existing `uicache -p` failed with `Path must be within /Applications/`; the staged app ID was not registered. The root filesystem was not remounted.

After the device began reporting `Activated` and Installation Proxy became available, the current UI bundle was packaged as a standard `Payload/BackThread.app` IPA and transferred through paired AFC to a uniquely named BackThread file in PublicStaging. Installation progressed through extraction, package inspection and preflight to 40% / `VerifyingApplication`, then returned:

```text
ApplicationVerificationFailed
ErrorDetail: -402620396
0xe8008014 (The executable contains an invalid signature.)
```

The ad-hoc signature's local page/resource integrity checks do not satisfy Apple's installer signing policy. No Apple development/distribution identity or matching provisioning profile was supplied for this project. No verification flags were bypassed. The exact own PublicStaging IPA was removed after the failed install.

## Final device state and cleanup

- No BackThread application, daemon or listener is running; port 23451 remains refused.
- Earlier binaries and the unregistered UI bundle remain staged under `/var/mobile/BackThread` for diagnosis; these precede the final static-audit fixes. They are not an installed or accepted release.
- The fresh session key remains at `/var/mobile/BackThread/session.key`, mobile-owned, mode 0600, inside the mode-0700 BackThread directory. Matching host key is in ignored `artifacts/ios-private/device-session.key`.
- `/var/mobile/backthread-device.tar.gz`, `/var/mobile/BackThread/app-test.tar.gz` and the native smoke probe were deleted, and their absence was confirmed over SSH. The former archive contained a transfer copy of the private key. Installation Proxy staging was also removed.
- Temporary Windows SSH/proxy connections ended. No launchd entry, firmware, activation setting, trust cache, sandbox policy, jailbreak component, unrelated app or personal file was changed.

## Runtime acceptance still blocked

| Test | Result |
|---|---|
| Native handshake, HMAC proof and heartbeat | Not run; no executable process |
| PCM reception, AudioQueue callbacks and speaker output | Not run; no audible output or listening confirmation |
| UIKit layout, gestures, media/actions/events | Not run; app not installed/launched |
| Native worker, file transfer and checksum | Not run |
| Process RSS, memory warnings, Jetsam, audio underruns | Unmeasurable for BackThread; it never reached `main` |
| App restart / disconnect recovery / screen-off | Not run; physical unplug/replug also pending |
| Sustained mixed workload, thermal/battery and acoustic latency | Not run |

## Windows and simulator preservation

Windows Host and simulator product sources are unchanged from `738a497`. The .NET Release regression build had zero warnings/errors; **19/19 regular tests and the shared C protocol/ring test pass**. The harness previously interpreted a zero-second soak setting as a one-second soak; this was fixed. An initial follow-up run's unintended one-second soak failed under concurrent build load (20 ms silence). The corrected default run excludes a soak. No new long soak or physical audio result is claimed.

The original checkpoint's 20/20 run, including its 120-second simulator soak, remains historical evidence in the existing reports. Earlier real Windows WASAPI and GSMTC results remain valid: 282,240 loopback PCM bytes / 24 callbacks, and real platform metadata/play/pause/seek events. They establish Windows behavior only.

Follow-up evidence: `reports/ios-device-initial.json`, `reports/ios-device-latest.json`, `reports/ios-installation-result.json`, `reports/ios-build-manifest.json`, `reports/ios-validation.json`, `reports/ios-artifact-tests.txt`, `reports/windows-preservation-results.json` and `reports/windows-preservation.txt`.
