# Resume from this checkpoint

## Primary hardware gate

Use a machine with an existing compatible iOS SDK/arm64 Theos or Apple toolchain. Build the current native sources before changing the architecture. Fix actual compiler/linker/resource/signing failures in place and record the exact toolchain/version. Do not mark Objective-C “compiled” until it really is.

Once the user-prepared device has an authorized installation path, install only the reviewed BackThread app package. Provision a fresh private key, launch the app and rerun `--diagnose`. The expected transition is from usbmux result 3 to an authenticated application connection on port 23451. Do not alter firmware, activation state, boot components or jailbreak services.

Run the physical acceptance sequence in BUILD_IOS.md, beginning with ping/hello and a short PCM fixture. Human speaker confirmation, physical unplug/replug and trust prompts are manual checks. Record them as pending until actually observed.

## Engineering priorities after the native build

1. Verify AudioQueue lifecycle and interrupt/route behavior under screen lock. Measure actual acoustic latency with a reproducible loop/recording setup.
2. Move native file writes/fsync off the socket reader with a strictly bounded utility I/O queue if device measurements show audio stalls. Test generation-safe replies across reconnects.
3. Validate the native quota/free-space checks on device, then add safe cleanup/retention for app-owned partial archives. Persist job spec fingerprints if cross-restart ID conflict rejection is required.
4. Align UTF-8 word-count semantics between C/Objective-C and .NET, then add Unicode boundary fixtures.
5. Run a 30-minute physical combined workload, collect actual RSS/Jetsam/thermal/battery evidence, then conduct a longer unattended test. Never disable Jetsam globally.
6. Test native touch → Host media/action → actual application behavior, large and missing artwork, fullscreen expiry/history, and UI memory warnings.
7. Add an opt-in packaged Windows notification consent flow; keep arbitrary app event integrations on the local API.
8. Complete AVPlayer handoff result/error reporting and coordinate PCM pause/resume before offering Mode C as an offload feature. Keep YouTube unsupported without a compliant provider.
9. Consider a worker-only launchd component only after device lifecycle evidence justifies it; define bounded authenticated app/daemon IPC and keep audio ownership in a supported process.

## Reproducible validation

`dotnet build BackThread.sln -c Release`, `scripts/test.ps1`, `scripts/test.ps1 -SoakSeconds 120`, shared C tests, hardware diagnostics, `--audio-probe` and `--media-self-test` are the existing entry points. See docs/reports for actual results, including the initial simulator-clock defect and the final corrected soak. The agent does not continue running development work after this chat ends.
