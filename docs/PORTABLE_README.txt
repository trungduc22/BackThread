BackThread 0.1.2 engineering checkpoint — Windows x64

Self-contained Host, native Tray, Simulator and DeviceTests are included. Keep
DLLs, runtime configuration and licenses together. No .NET SDK/runtime install
is needed to run these executables. No private key is shipped.

1. Open PowerShell in this directory. Run Setup-Portable.ps1 -Transport usb
   (or -Transport localhost for simulation). Keep config/session.key private.
2. An iOS app must actually run with the matching key. The session's phone now
   previously ran the embedded Agent and received nonzero PCM buffers, but
   the latest fixture and ordinary Windows media were heard. Media still
   stutters; newest Windows batching needs physical retest after unplug.
   Do not repair missing system/jailbreak components. Wait for reconnection.
3. Start BackThread.Tray.exe OR BackThread.Host.exe --config config/local.json.
   Do not run both on the same API port.
4. Simulator: BackThread.Simulator.exe --key-file config/session.key

Physical acceptance gate (no sound by default):
 BackThread.DeviceTests.exe --key-file config/session.key
After the prepared app actually executes, a quiet fixture is explicit and short:
 BackThread.DeviceTests.exe --key-file config/session.key --fixture --seconds 3 --worker
Use --simulator --port 23461 only with an explicitly started local simulator.
Reports distinguish physical USB from simulation; listening, visual UI and cable
unplug are not automatically certified. See DEVICE_ACCEPTANCE.md in the source.

Windows-only probes:
 BackThread.Host.exe --diagnose
 BackThread.Host.exe --audio-probe
 BackThread.Host.exe --media-self-test

The HTTP API is loopback-only at /v1 on port 23452, with bearer authentication
using the matching lowercase hex session key. No cloud, Wi-Fi or iPhone internet
is required for USB PCM/jobs. No kernel sound driver is installed. This remains
an engineering checkpoint with explicit native execution/audio limitations.

The updated runner starts timing at PCM pacing, excludes late/shutdown stats,
and reports zeroLossInSampledWindow separately from functional checks. Use
--require-nonzero with an intentional known Windows source for loopback.
Receiver audio.error is visible as audioError in /v1/status; it does not
certify sound or trigger system repair. Reports/releases are engineering
checkpoints, not a declaration of full hardware acceptance.
