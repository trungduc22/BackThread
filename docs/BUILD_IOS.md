# iOS 12 build and authorized installation

**No iOS binary or package was built in this session.** This Windows environment has no usable WSL distribution, Theos installation, Apple SDK, arm64 Apple linker/signing chain, or reachable SSH service. The native sources are not a substitute for an actual build result.

## Required external environment

- macOS with a compatible Apple iOS SDK/toolchain, or an existing Theos Linux/WSL setup with a legally available compatible SDK and arm64 toolchain.
- Theos makefiles, make, linker, package utilities, and appropriate application signing configuration.
- iPhone 6 / iOS 12.5.8 already prepared for the selected authorized installation method. Jailbreak and device preparation remain outside BackThread.

Follow [Theos installation instructions](https://theos.dev/docs/installation-linux). Do not install or change a jailbreak to make this repository build. No external toolchain/SDK is vendored in the project.

## Build

The Makefile targets arm64 with an iOS 12.0 minimum and a rootful `/Applications` application bundle, appropriate to the requested iOS 12 target. There is no tweak injection, launchd daemon, SpringBoard hook or unrelated service modification.

```bash
export THEOS=/path/to/existing/theos
# Ensure an externally configured rootless scheme is not applied to this iOS 12 target.
unset THEOS_PACKAGE_SCHEME
bash scripts/build-ios.sh
```

The script invokes `make clean package FINALPACKAGE=1` in `src/iOS`; packages, if successful, appear under Theos's project packages directory. It deliberately does not install anything. Do not distribute a package until it actually compiles and completes the physical checks below. Apple framework availability/signing/resource packaging must be verified by that build.

## Native layout

- `Agent/BTAgent.m`: loopback accept/auth/frame dispatch, file storage, bounded worker, journal and UI event delivery.
- `AudioReceiver/BTAudioReceiver.m`: AVAudioSession playback and AudioQueue output, bounded ring, interruptions and media-service reset handling.
- `UI/BTViewController.m`: landscape media controls, native volume/routes, actions, job status/history and transient notifications.
- `Resources/Info.plist`: application identity, iOS minimum, orientations and audio background mode.
- `../Shared/Protocol/bt_wire.c`: framing also compiled in the Windows native core regression.

## Install and provision

Use only the user's existing authorized app/package installation method. If a prepared device already supports package installation via SSH, copy the reviewed built package and install that application package using that environment's normal package manager. Do not restore, erase, modify the boot chain, alter jailbreak components or disable Jetsam. No automatic install command is run by this repository.

Launch BackThread. Tap Pair and privately provision the host-generated 64-character hex key. Save, close and reopen the app. The key resides in the app's `Library/BackThread/session.key` with mode 0600 and protection until first unlock. No listener starts until a valid key is present. The app's files and job journal stay in `Documents/BackThread`.

Keep the app open for the first connection. With `transport: "usb"`, Host reaches port 23451 through the single Lightning cable. No internet is required for PCM, media control, events or jobs.

## Physical acceptance still required

1. Build/link/sign/package successfully and verify it launches on the actual A8 device.
2. Authenticate and exchange ping/control messages; reject wrong keys and malformed frames.
3. Confirm audibility with a human, then test screen-off playback, interruptions, output routes, volume and reconnects.
4. Transfer/verify files, cancel jobs and restart the app during partial transfers.
5. Measure process RSS, memory-warning behavior, Jetsam logs, battery/thermal impact and at least a 30-minute mixed workload.
6. Check landscape Auto Layout on the 667×375-point phone, gestures, notification expiry/history, artwork, media commands and action authorization.

No audio output, launchd behavior, native memory footprint, independent internet handoff or physical disconnect action is marked verified before these checks run. Background workers can be suspended when audio stops or the app is terminated; the current design does not guarantee daemon-like execution.
