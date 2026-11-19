# Audio follow-up · 2026-11-19

This checkpoint follows `69dab7b` on `codex/native-foundation`. See the [foundation report](NATIVE_FOUNDATION_REPORT.md) for the preceding branch, CI and toolchain results.

## Reproduced problem

The simulator's socket reader called `Jobs.Submit`, `Jobs.Query` and `Jobs.Cancel` directly. Submitting synchronously wrote and atomically replaced the job journal under the scheduler lock. Query/cancel could also wait for that lock while the worker persisted progress. Any of those waits also delayed PCM/control frame processing.

The new regression fixture holds the actual journal write path before the write starts, then sends PCM and a heartbeat over the same authenticated connection. With the old dispatch path, the heartbeat hit its two-second deadline and the test exited 1 (0/1). After the change, the simulator acknowledged the heartbeat and counted all three PCM packets while the job reply was still pending. It then completed the job when the barrier opened. This establishes one blocking path without attributing every underrun to it.

## Changes

- The simulator processes job requests in order on a separate queue, with seven waiting slots and one active command. Admission fails explicitly when full. Its existing bounded file queue now handles directory listing.
- Both writer paths report the largest gap between completed PCM writes and the sequence number after that gap. Simulator stats include the sequence number after the largest PCM arrival gap. Only the peaks are kept. Matching peaks can establish that a stall was already present before the receiver saw the packet. Different peaks are evidence of different observations and do not isolate all causes.
- The phone code had the same source-level problem. Native submission wrote its journal inline under the same lock used to check PCM ownership. Job commands now enter a separate serial queue with eight total slots, and journal I/O uses a job-state lock. The shared worker admission count still uses the short agent lock. Delayed job replies retain their original connection generation. Existing job IDs, journal path, on-wire operations, PCM buffer sizes and UI layout are preserved.

## Results

- Windows Release solution built with zero warnings and errors. The managed suite passed 38/38 and all three shared C suites passed. Python passed 31 checks, including nine against the freshly built iOS artifacts. Clang analysis completed all 19 translation units with no findings.
- The isolated strict soak exited 1. It ran 120.24 seconds and processed 205 upload/hash jobs. The receiver inserted 1,920 bytes of zero fill (10 ms of 48 kHz stereo PCM). Receiver overflows, sequence gaps, sender queue drops and skipped render ticks were all zero. The maximum inter-arrival gap was 56.245 ms ending at sequence 7011. The maximum completion gap across the sender's socket writes was 25.321 ms ending at sequence 11458.
- The residual stall is visible downstream of the sender's writes in this loopback run. The counters do not separate OS delivery, receive continuation scheduling and remaining receive handler work. The least invasive next measurement is to record enqueue, write completion, read completion and PCM acceptance for the same sequence around the underrun. The buffer and strict thresholds stayed the same. Single runs under different workloads are insufficient to claim a durable performance gain.
- Build 11 is an ARM64/iOS 12 engineering artifact. Its prepared IPA is `artifacts/audio-followup/ios/BackThread.ipa`. SHA-256 is `94a08b584e60f2a433fb3f77a4df0c1662f9d618ef0f93471f6ee46a09393768`. Device execution, listening, Wi-Fi and audio session behavior remain pending.

Raw timing and loss snapshots are in [audio-journal-followup.json](reports/audio-journal-followup.json).

## Repeat locally

Run from the foundation worktree with .NET 8. These checks run on the PC and do not require a device.

```powershell
pwsh -NoProfile -File scripts/test.ps1
$env:BACKTHREAD_TEST_FILTER='soak.concurrent-audio-worker'
pwsh -NoProfile -File scripts/test.ps1 -SoakSeconds 120
Remove-Item Env:BACKTHREAD_TEST_FILTER
```

Stop concurrent builds before the timing run. Keep simulator measurements separate from iPhone speaker quality, WASAPI capture and USB timing. Physical verification remains pending.
