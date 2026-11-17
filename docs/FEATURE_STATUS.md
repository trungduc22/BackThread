# Feature acceptance matrix

Evidence date: 2026-11-18, Asia/Saigon. “Real hardware” below names the specific side: Windows checks do not imply an iPhone application test.

Categories: **1** Implemented and tested on real hardware; **2** Implemented and tested in simulation; **3** Compiled but not runtime-tested; **4** Implemented but not compiled; **5** Partially implemented; **6** Blocked; **7** Not implemented.

| Feature | Category | Actual evidence / remaining boundary |
|---|---|---|
| USB device enumeration and application-port connection attempt | 1 — Windows + attached iPhone diagnostics | Managed Host and Python diagnostic see the real iPhone 6 / iOS 12.5.8 through Apple service. Application port is refused; no application handshake occurred. |
| Local pair-record discovery | 1 — Windows diagnostic | Pair record exists. Current trust session, jailbreak state and install authorization are not validated. |
| Session authentication, framing, heartbeats, multiplexing | 2 | Actual TCP socket tests; fragmentation, size/version/channel errors, wrong key and replay rejection. Native CommonCrypto authentication remains category 4. |
| Automatic Host reconnect | 2 | Running Host/HTTP API recovers from simulator-injected disconnects. Physical cable unplug/replug is pending. |
| WASAPI capture and PCM format conversion | 1 — Windows | Real render/capture probe obtains nonzero 48 kHz PCM16 stereo on Windows 10 build 19045. |
| PCM transport and bounded playback model | 2 | Deterministic sample hash/order test plus two-minute mixed-workload soak; final run had zero measured gaps, overflow or underrun silence. |
| Shared C framing and PCM ring | 2 | Same C sources compiled/executed on Windows using GCC with warnings as errors. Not an iOS/arm64 build. |
| Native AVAudioSession/AudioQueue playback, volume, interruptions | 4 | Source targets iOS 12. No Apple SDK/toolchain or running app. |
| End-to-end iPhone speaker output / screen-off / routes | 6 | Agent cannot run here; no audible output or human listening confirmation. |
| GSMTC metadata, source, play/pause and seeking | 1 — Windows | Silent native Windows fixture exposes real media session; actual command events verified. Phone touch path remains untested. |
| GSMTC previous/next and artwork | 3 | Compiled capability-aware implementations and bounded thumbnail conversion. No real playlist/artwork acceptance run. |
| Generic event priorities, masking, dedup, expiry/history | 2 | Unit and socket integration tests with privacy and flood checks. |
| Windows official notification collector | 5 | Collector compiles; MSIX identity/capability and interactive consent flow are absent. Opt-in API works instead. |
| Quick Action registry and authorization | 2 | Registry transmitted in Host integration; local API rejects unlisted actions. |
| Configured launch and microphone-mute actions | 3 | Native Windows implementations compile; no application launch or microphone state change used as an acceptance test. |
| Switch default Windows audio output | 7 | No supported endpoint-switch implementation selected. |
| Native UIKit dynamic media/actions/worker UI | 4 | Source only; no device layout, gestures, artwork, responsiveness or visual verification. |
| Native fullscreen/banner/history presentation | 4 | Source with expiry and auto-return; not compiled or shown on the phone. |
| SHA-256, integrity, metadata, text and named diagnostics worker | 2 | Simulator executes real streamed operations; success, failure, cancellation, dedup, restart and low-memory tests pass. Native implementation is category 4. |
| Binary file upload/download, checksums and resume | 2 | Real socket upload, complete SHA-256, partial resume, duplicate/out-of-order chunks and range retrieval tested. Native implementation is category 4. |
| Archive backup | 5 | Opaque archive storage/transfer implemented; directory snapshot creation, retention policy and block-delta incremental backups absent. |
| SSH integration | 3 | Compiled OpenSSH utility with strict key/known-host options and diagnostic allowlist. Actual device port 22 is refused. |
| GhostCode SubmitJob/CancelJob/status/archive/artifact/events/progress API | 2 | Host API/auth and worker protocol tested; packaged-process smoke test verifies SSE, archive/checksum/artifact round trip and recovery after an agent-process restart. No GhostCode installation or repository/database access required. |
| Handoff abstraction and URL validation | 2 | Connectivity/direct-HTTPS/YouTube rejection tests pass. |
| Native direct AVPlayer handoff | 5 | Source receipt/play invocation only, no successful playback/result handshake or coordinated PCM pause/resume. |
| Independent YouTube playback/offload | 7 | No provider extraction or unrestricted background YouTube support. Modes A/B remain the reliable architecture. |
| Background worker independent of UI termination | 7 | No launchd daemon installed or packaged; UIKit background audio is the current process-lifetime mechanism. |
| Memory-pressure scheduling | 2 | Simulator pauses/rejects optional work and cancellation still works. Native warning/cache code is category 4. |
| Actual iPhone RSS/Jetsam/thermal/battery measurements | 6 | App cannot execute. Never disabled Jetsam or changed phone services. |
| Windows tray | 3 | WinForms app compiles and publishes; native interactive tray UI has not been manually inspected. |
| Windows portable packaging | 1 — Windows command-line processes | Self-contained Host and simulator run without the system .NET 8 runtime; real packaged-process API, SSE, file and restart checks pass. Tray UI remains category 3. |
| iOS package/install/log collection | 6 | No iOS toolchain, reachable SSH, signed app or authorized executable deployment path available in this session. |

Overall: functional, tested Windows components and simulator with substantial native iOS source. Final end-to-end hardware acceptance has **not** been achieved.
