# Performance and stability evidence

Measurements were collected on Windows 10 build 19045. Simulator measurements are not iPhone RAM, CPU, acoustic latency or power measurements.

## Final strict mixed workload

Command: `scripts/test.ps1 -SoakSeconds 120`, using SDK 8.0.428. The complete managed run passed **20/20 checks**, including the strict soak; shared native C tests also passed. See [acceptance results](reports/acceptance-results.json) and [final metrics](reports/soak-primed-metrics.json).

| Metric | Observed |
|---|---:|
| Elapsed workload | 120.0243569 seconds |
| Completed upload/commit/remote SHA-256 cycles | 241 |
| Archive size per cycle | 4 MiB |
| Total uploaded fixture data | 964 MiB, replacing one stored file |
| PCM packets / bytes | 12,003 / 23,045,760 |
| Wire sequence gaps | 0 |
| Sender drops | 0 |
| Receiver overflow bytes | 0 |
| Receiver underrun silence after priming | 0 |
| Maximum sampled receiver buffer | 9,600 bytes = 50 ms |
| Final receiver buffer | 7,680 bytes = 40 ms |
| Combined working set, start | 97,624,064 bytes, about 93.1 MiB |
| Combined working set, end | 98,734,080 bytes, about 94.2 MiB |
| Process CPU time | 87.28125 CPU seconds |

CPU covers sender and simulator together, including repeated hashing, transfers and fixture synthesis: roughly 0.73 logical core on average, not a percentage of the whole processor or a phone CPU estimate. The two memory snapshots differ by about 1.1 MiB; this short run does not prove absence of a leak. The iPhone sub-150-MB goal remains unmeasured. Fixture transfer throughput is not a physical USB benchmark.

## Failures investigated and repaired

The first soak checked only bounded capacity, while its counters recorded substantial receiver overflow. It was not accepted as stable audio. Windows callbacks coalesced, but the initial simulated DAC consumed only one 10 ms block per callback. It now follows elapsed sample time.

A scoped multimedia-timer request improved Windows pacing. A clean two-minute timer-only run was followed by a full mixed run that exposed 60 ms overflow and 110 ms silence under load; the strict gate failed. Sender pacing and simulated DAC timing were moved to dedicated threads. A remaining initial 10 ms gap came from starting the sample clock at negotiation, before the first PCM packet. Playback now starts from the first packet with initial priming. Native AudioQueue source has matching priming, pending iOS validation.

Historical reports preserve the initial counters, clock correction, earlier clean timer-only run, strict pre-priming failure and dedicated-thread pre-priming failure. The authoritative final gate is acceptance-results.json with soak-primed-metrics.json.

timeBeginPeriod(1) is paired with timeEndPeriod(1) for stream lifetime only. This can increase power use; on earlier Windows 10 versions it may temporarily affect global resolution. No persistent system-security setting changes.

## Real Windows tests

The WASAPI render/capture probe returned 282,240 bytes, including 244,486 nonzero bytes at 48 kHz stereo PCM16. A separate actual loopback → production LoopbackBridge/protocol → simulated receiver test received 189 packets / 362,880 bytes, no sequence gaps or overflow, and a hash different from all-zero PCM. That test queried stats after intentionally cancelling the sender, including one final 10 ms silence block at shutdown; it is not claimed as uninterrupted phone playback. See [bridge metrics](reports/windows-bridge-metrics.json).

A silent Windows media fixture verified actual GSMTC metadata and play/pause/seek events. These tests do not measure phone latency, sound quality, background behavior or human listening results.

## Bounds and missing measurements

PCM rings are 19,200 bytes (100 ms). Transport queues hold at most six 10 ms audio packets, thirty-two bounded control frames and two file frames. Native AudioQueue has three 1,920-byte buffers and 30 ms initial priming. File chunks are at most 32 KiB. These are capacities, not end-to-end latency claims.

No overnight stability, acoustic loopback, actual iPhone RSS/Jetsam, battery/thermal, physical USB throughput or verified workload-reduction measurement exists. Those require a running device app.
