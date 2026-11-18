# Device acceptance runner

`tests/BackThread.DeviceTests` uses production UsbMux, Authentication, Connection, LoopbackBridge and RemoteWorker code. The Windows release includes its self-contained executable. It does not substitute a simulator for a phone silently. Default transport is physical USB; `--simulator` is explicit and is recorded in every report.

From the repository, with the existing private diagnostic key:

```powershell
dotnet run --project tests/BackThread.DeviceTests -c Release -- `
 --key-file artifacts/ios-private/device-session.key `
 --output artifacts/device-acceptance.json
# Only after native entry/handshake works:
dotnet run --project tests/BackThread.DeviceTests -c Release -- `
 --key-file artifacts/ios-private/device-session.key --fixture --seconds 3 --worker `
 --output artifacts/device-audio-acceptance.json
```

In a portable release, run `BackThread.DeviceTests.exe` with the same arguments and your matching key file. No key is shipped. `--fixture` is limited to five seconds and defaults to queue gain 0.08 on the real Agent (explicit --gain accepts 0..0.3); it never raises system output volume. `--loopback --seconds 30` uses real WASAPI instead. Choose one source, or omit both for protocol/worker checks without audio. The duration limit is 180 seconds; longer physical stability testing belongs in the ordinary Host workflow.

The runner checks mutual authentication, wrong-key rejection/recovery, unsupported framing version, hello classification, three heartbeats, event/dedup reception, named memory diagnostics, PCM counters, and new-socket authentication after cleanup. Real audio additionally requires callbacks, noErr, running/primed state and the built-in speaker route. `--worker` generates one unique one-MiB fixture, verifies the whole SHA-256 and a byte-exact range, then deletes the local fixture. The report names the remaining app-owned phone/simulator fixture for scoped cleanup; no general file-delete protocol was added. Memory snapshots are evidence, not a leak or footprint certification.

`passed:true` describes the checks actually run. It does not establish UIKit rendering, human audibility, acoustic latency, Windows media touch control, physical unplug, app restart or extended native stability. These fields remain explicitly unverified. Recorded physical runs subsequently authenticated, executed jobs and rendered nonzero buffers. Earlier two human listening checks failed. The latest native repair build was upgraded: a new fixture and normal WASAPI media were heard, with stutter on normal media. Earlier port-refusal reports are historical.

Explicit simulator exercise, using two terminals:

```powershell
BackThread.Simulator.exe --key-file config/session.key --port 23461 --state state/acceptance
BackThread.DeviceTests.exe --simulator --port 23461 --key-file config/session.key `
 --fixture --seconds 3 --worker --output artifacts/simulator-acceptance.json
```

The current explicit simulator run passed 18 checks, including simultaneous PCM/file work and a new session. It also exposed a runner assumption: simulator audio has no hardware volume control. The runner now requests gain only on the real Agent. That does not fabricate a successful volume test.

Exit codes: 0 checked flow passed; 1 connected flow failed; 2 no authenticated Agent was available or initial setup failed. A USB report cannot pass if hello identifies a simulated peer. Reports omit device serials, session keys and authentication proofs. Run only one audio owner/Agent listener at a time.

Current timing starts at the pacing thread after audio.start. One source deadline governs cancellation and sample admission. Polls are absolute, missed polls are skipped, and responses outside the active window are excluded. Reports contain audioWindowSeconds and audioSampleTimesSeconds; zeroLossInSampledWindow is a counter observation, not acoustic or extended quality acceptance. --require-nonzero additionally checks native input/render peaks for an intentionally supplied WASAPI signal. Native getter status fields require the latest repair app; older installed diagnostic builds lack them.

New native audio.error means PCM playback failed while USB control/jobs remain available; Host status exposes audioError. On a failed queue, pending PCM is discarded until an explicit successful audio.start. A failed audio.volume request returns error rather than false success. Native reset/route/stop behavior still needs physical verification.
