# Performance Evidence — 0.2.0

## Phase 9 Zero-Loss Live PCM Simulator Soak & Resumed 60s Physical Baseline (2026-11-18)

Prior `6b986ce` / `6c2fb35` measurements captured a bounded 60-second ordinary WASAPI physical playback run (`Có tiếng, nghe liền mạch`, `6,000` frames, `0` sampled receiver gaps/overflow/silence/sender drops, RSS `12.48–12.57 MiB`, app gain `0.25`, `17,416` unlocalized Windows capture zero-fill bytes, and `9,600` silence bytes on the pre-Phase-9 120s simulator soak [`resume-soak-file-io-metrics.json`](reports/resume-soak-file-io-metrics.json)). See [`resume-physical-wasapi60.json`](reports/resume-physical-wasapi60.json) and [`resume-human-listening.json`](reports/resume-human-listening.json).

Prior `0.1.2` simulator measurements ([`repairs-soak-final-metrics.json`](reports/repairs-soak-final-metrics.json)) failed the strict zero-loss gate during heavy Channel 6 worker load (`118` sender drops, `20` gap events, `5,760` overflow bytes, `243,840` silence bytes over `120.33s` / `98` 4-MiB jobs).

After diagnosing and repairing four pipeline bottlenecks (`AgentServer.cs` async Channel 6 flush worker isolation, `Connection.cs` non-allocating `TryWrite` fast paths & reusable CTS with `8`-frame audio queue, `LoopbackBridge.cs` 250 ms capture ring + sub-ms `Stopwatch` pacing without destructive `50ms` resets, and `BTAudioReceiver.m` `_underrunStreak >= 2` queue re-priming):

| Run | Duration | Concurrent 4-MiB Jobs | PCM Packets / Bytes | Peak Buffer (`<=19,200`) | Sender Drops | Receiver Gaps | Overflow / Dropped | Silence Bytes | Result |
|---|---|---|---|---|---|---|---|---|---|
| Historical baseline (`repairs-soak-final-metrics.json`) | 120.33s | 98 | 11,901 / 22.85 MB | 19,200 B | 118 | 20 | 5,760 B | 243,840 B | Fail |
| Phase 9 15s Baseline (this session) | 15.17s | 48 | 1,517 / 2.91 MB | 9,600 B | 0 | 0 | 0 B | 0 B | **Pass (Sim)** |
| Phase 9 25s Full Suite (`task-678`) | 25.19s | 82 | 2,520 / 4.84 MB | 9,600 B | 0 | 0 | 0 B | 0 B | **Pass (Sim)** |
| **Phase 9 30s Soak ([`hybrid-soak-30s.json`](reports/hybrid-soak-30s.json))** | **30.15s** | **98** | **3,014 / 5.79 MB** | **9,600 B** | **0** | **0** | **0 B** | **0 B** | **Pass (Sim)** |

CPU & Memory snapshot during the 30.15s concurrent 98-job (`~392 MiB` hashed) + continuous audio run on Linux: `7.97s` total process CPU time across `30.15s` wall time; receiver buffer capacity stayed at **50% (`9,600 / 19,200 B`)** with **zero** frame drops or silence fill.

## Historical Physical iPhone 6 Hardware Checkpoint (`0.1.2` / `307e167`)

- **Three-second direct PCM fixture**: Audible speaker confirmation (`Có nghe tiếng`), Speaker route, 48 kHz, app gain `0.25`, peaks `4096`, `noErr`, zero sampled gaps/overflow/underrun ([`repairs-physical-fixture.json`](reports/repairs-physical-fixture.json)).
- **90-second pre-Phase-9 diagnostic WASAPI run**: Heard with user-reported stutter (`Có nhưng bị ngắt/giật`), 472 sender drops, 132 gap events, 1,416,960 overflow bytes, 2,423,040 silence bytes; fifteen native iOS RSS samples ranged `11,337,728–11,395,072` bytes (~10.8 MiB) ([`repairs-physical-wasapi90.json`](reports/repairs-physical-wasapi90.json)).
- **Normal Host snapshot before unplug**: `0.046875s` CPU over `3.04s` wall time, `84,193,280` bytes RSS ([`repairs-host-load.json`](reports/repairs-host-load.json)).

## Audio Ring & Memory Parameters Preserved

- Native C ring (`BTPcmRing.c` / `BT_PCM_CAPACITY`): **19,200 bytes** (`100 ms` at 48 kHz stereo PCM16); initial prime **5,760 bytes** (`30 ms`); **three 1,920-byte** `AudioQueue` buffers (`10 ms` each).
- Windows capture ring (`LoopbackBridge.cs`): **48,000 bytes** (`250 ms`), accommodating NAudio shared-mode burst intervals up to ~100 ms without dropping samples before 10 ms transport pacing.
- iOS Mode B cache (`BTBufferedMediaEngine.m`): Bounded **24 MiB** LRU memory/disk window (`Caches/BackThreadBuffered`) with instant memory-warning purge for iPhone 6 `1 GB` RAM headroom.

## Still Required on Physical Hardware

Phase 9 simulator results prove transport/queue decoupling under synthetic Linux socket load, NOT acoustic playback over physical Apple `usbmux`. Re-running `BackThread.DeviceTests.exe --require-nonzero` + 30-minute physical continuous playback, thermal/RSS soak with live `AVPlayer` External PiP, route changes, lock-screen continuity, and cable unplug/replug recovery remain mandatory once physical hardware is attached.
