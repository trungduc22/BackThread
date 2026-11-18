# Windows build and operation

## Toolchain

Use Windows 10 1809+ x64 and a .NET 8 SDK. The current session used Windows 10 build 19045 and SDK 8.0.428. A matching .NET runtime alone is insufficient for building. Install the SDK normally or keep an official, checksum-verified portable SDK in a separate directory and pass its executable to the scripts.

```powershell
./scripts/build.ps1 -Dotnet 'C:\Tools\dotnet\dotnet.exe'
./scripts/test.ps1 -Dotnet 'C:\Tools\dotnet\dotnet.exe' -SoakSeconds 120
./scripts/build.ps1 -Publish
```

Publish produces a shared `artifacts/release/Windows` directory with self-contained win-x64 Host and Tray executables. This does not install drivers, a service, a scheduled task, registry entries, or a startup item. Preserve every runtime file and third-party notice. Do not copy a single EXE out of the directory and expect it to run.

The delivered portable ZIP also includes the simulator. From its extracted directory, `powershell -File Setup-Portable.ps1 -Transport usb` creates a fresh private key and local configuration without overwriting existing files. Select `localhost` for a simulator setup. No SDK is required for the self-contained executables.

## Configure and start

```powershell
# In the published directory, or replace .\BackThread.Host.exe with dotnet run from source.
./BackThread.Host.exe --init-key config/session.key
Copy-Item config/example.json config/local.json
./BackThread.Host.exe --diagnose
./BackThread.Host.exe --config config/local.json
# Or use the native tray (do not run both coordinators on the same API port):
./BackThread.Tray.exe
```

The tray expects `config/local.json` under its application directory. A single explicit configuration path may also be passed. From source, run `dotnet run --project src/Windows/Tray -- <absolute project config path>`. Relative key paths resolve from the project/published root, so keep the standard config subdirectory.

`transport` is `usb` by default; use `localhost` only for the simulator. Set `deviceSerial` if more than one USB iPhone is attached. Never put private credentials in example.json or checked-in files. Host logs capabilities with a `simulated` field and never presents simulator success as physical playback.

Apple Mobile Device Service must be installed and running, normally through Apple Devices or iTunes. Diagnostic failure is reported without installing or restarting services. The application port is 23451, not SSH. Device trust/jailbreak/install preparation must be completed separately through an authorized method.

## Diagnostics

```powershell
./BackThread.Host.exe --diagnose
./BackThread.Host.exe --media-probe
./BackThread.Host.exe --audio-probe
./BackThread.Host.exe --media-self-test
python scripts/diagnose-hardware.py --output artifacts/hardware-audit.json
```

The audio probe renders a very quiet deterministic 1.5-second tone through the default Windows multimedia endpoint, captures its real loopback stream at 48 kHz stereo PCM16, and reports counts. It does not test the iPhone. The media self-test creates a temporary silent WAV/player session and verifies actual GSMTC metadata/play/pause/seek events, then disposes the player and file. Neither probe requires a media service account.

## Tests

`scripts/test.ps1` builds the executable regression runner, executes socket tests and optionally a soak, then compiles/runs the shared native C test when GCC is available. Failures produce a nonzero exit code. The runner supports `BACKTHREAD_TEST_FILTER` for targeted tests and `BACKTHREAD_TEST_REPORT` for an alternate JSON report path. `BACKTHREAD_SOAK_SECONDS=120` enables the concurrent fixture-audio/transfer/hash workload. The normal report does not include acoustic latency or iPhone process memory.

Set `BACKTHREAD_WINDOWS_PROBES=1` and filter `windows.actual` to run the optional real WASAPI → production PCM bridge → simulated receiver test. It renders the same quiet tone and checks that received PCM is nonzero. This adds real Windows capture evidence while keeping the iPhone sink explicitly simulated.

After publishing, `python tests/portable_smoke.py` executes the self-contained Host/Simulator, checks API authorization, SSE, uploads/integrity/artifacts/privacy, restarts its own simulator process and verifies reconnection/persistence. Credentials and process state stay under the ignored artifacts directory.

## Notifications and SSH

Windows notification collection is off by default. The collector reads only when UserNotificationListener access is already Allowed. This unpackaged build cannot provide the required declared capability and interactive consent UI; use the authenticated local `/v1/events` API. An MSIX package/consent flow is future work. Do not scrape other apps or their databases.

For SSH utilities, use a separately prepared OpenSSH service on the iPhone and a loopback-only port forward through an installed external iproxy. Verify its command syntax/version, enroll the device host key through a trusted channel, and use the `SshUtilities` library with an explicit identity and known-hosts file. Historical diagnostics successfully used the existing USB SSH service on port 44 with strict isolated host-key verification and explicitly supplied credentials. Product SSH utilities remain separately classified; no password/key is bundled. Current phone services must be rechecked only after user readiness.

Normal USB Host explicitly sets configured audioGain (default 1, valid 0..1) before audio.start. This controls only the app's AudioQueue gain and restores normal playback after quiet diagnostic sessions; it does not set the iPhone's system volume. Fixture tests retain their separate deliberately low gain limit. Keep volume control on the phone at a comfortable level.
