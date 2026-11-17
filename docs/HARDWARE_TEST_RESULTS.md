# Hardware test results

Date: 2026-11-18, Asia/Saigon. Raw evidence is in `docs/reports`.

## Environment observed

- Windows 10 x64, build 19045.
- Existing system .NET host/runtime: 10.0.15, initially no SDK.
- Downloaded workspace-local .NET 8 SDK 8.0.428 from Microsoft; verified official SHA-512 `cce1dc5cfb2c5b73ddd9d3658f6009deb85be3a58dbec44a6dfe3910bb32b044ad2fc6bb28c77aded70c73270a204d207fe790dcb82ffca6c7b42c1335b51c21`.
- Git, Python, MinGW GCC and Windows OpenSSH are available. No usable WSL distribution, Theos, Apple SDK or arm64 iOS build chain was found.
- Apple Mobile Device USB Driver is present and reports OK. Apple Mobile Device Service is running and accepts usbmux plist traffic on 127.0.0.1:27015.

## Physical iPhone diagnostics actually executed

| Check | Result |
|---|---|
| USB enumeration | PASS, physical USB device listed by Apple service |
| Product type | `iPhone7,2` (iPhone 6) from lockdown GetValue |
| iOS version | `12.5.8` from lockdown GetValue |
| Device class | `iPhone` |
| Local pairing record | Present; private contents not exported or logged |
| Current trusted lockdown session | Not validated |
| Lockdown port 62078 | usbmux result 0, connection established |
| SSH port 22 | usbmux result 3, unavailable/refused |
| BackThread port 23451 | usbmux result 3, unavailable/refused |
| Jailbreak status | Not established by these diagnostics |

An ActivationState read was prohibited by the device; no activation operation or bypass was attempted. No device state, firmware, trust database, jailbreak component or launch service was changed.

## Real Windows runtime tests

WASAPI probe rendered a quiet deterministic tone and captured actual loopback at 48,000 Hz / 2 channels / 16 bits. It returned **282,240 captured bytes**, **244,486 nonzero bytes**, and **24 callbacks** on the Realtek Speakers / Headphones endpoint. These values establish functioning Windows capture/conversion, not audio quality or iPhone output.

A silent five-second local media fixture exposed an actual Windows GSMTC session. Metadata was discovered and play, pause and seek commands each generated the expected platform event. The fixture/player/file were disposed afterward. This does not establish third-party app compatibility beyond the participating-session API contract.

## iPhone application tests not run

No native app build, install, launch, authenticated BackThread session, heartbeat, audio reception, speaker output, native UI interaction, file job, app restart, process-memory measurement, syslog collection or sustained phone workload could run. No human listening check or physical unplug/replug action was performed.

All successful application-level transport/audio/worker tests in the regression and soak reports use the local protocol simulator. The physical device was not substituted with a fabricated success record.
