# Feature acceptance matrix

Evidence date: 2026-11-18, Asia/Saigon; native follow-up from `738a497`. “Real hardware” names the specific side: Windows checks and successful iOS compilation do not imply an iPhone application test. The actual phone rejected standalone execution and normal app signature verification; no BackThread code reached main.

Categories: **1** Implemented and tested on real hardware; **2** Implemented and tested in simulation; **3** Compiled but not runtime-tested; **4** Implemented but not compiled; **5** Partially implemented; **6** Blocked; **7** Not implemented.

| Feature | Category | Actual evidence / remaining boundary |
|---|---|---|
| USB device enumeration and application-port connection attempt | 1 — Windows + attached iPhone diagnostics | Managed Host and Python diagnostic see the real iPhone 6 / iOS 12.5.8 through Apple service. Application port is refused; no application handshake occurred. |
| Paired lockdown TLS and service discovery | 1 — actual iPhone diagnostics | Existing pair record successfully establishes TLS with exact paired-device certificate pinning. Latest state Activated; AFC/Installation Proxy/syslog available, AFC2 unavailable. This does not establish signing/execution authorization or a complete jailbreak environment. |
| Session authentication, framing, heartbeats, multiplexing | 2; native 3 | Actual simulator TCP socket tests; fragmentation, size/version/channel errors, wrong key and replay rejection. Native CommonCrypto/transport compiles and analyzes cleanly but has no device handshake. |
| Automatic Host reconnect | 2 | Running Host/HTTP API recovers from simulator-injected disconnects. Physical cable unplug/replug is pending. |
| WASAPI capture and PCM format conversion | 1 — Windows | Real render/capture probe obtains nonzero 48 kHz PCM16 stereo on Windows 10 build 19045. |
| PCM transport and bounded playback model | 2 | Deterministic sample hash/order test plus two-minute mixed-workload soak; final run had zero measured gaps, overflow or underrun silence. |
| Shared C framing and PCM ring | 2; native 3 | Same C sources compile/execute on Windows using GCC with warnings as errors; also compile as iOS 12 ARM64 and pass Clang analyzer. No phone execution. |
| Native AVAudioSession/AudioQueue playback, volume, interruptions | 3 | Actual iOS 12 ARM64 compile/link and analyzer pass. Error/callback/route statistics added; runtime audio APIs have not executed. |
| End-to-end iPhone speaker output / screen-off / routes | 6 | Agent cannot run here; no audible output or human listening confirmation. |
| GSMTC metadata, source, play/pause and seeking | 1 — Windows | Silent native Windows fixture exposes real media session; actual command events verified. Phone touch path remains untested. |
| GSMTC previous/next and artwork | 3 | Compiled capability-aware implementations and bounded thumbnail conversion. No real playlist/artwork acceptance run. |
| Generic event priorities, masking, dedup, expiry/history | 2 | Unit and socket integration tests with privacy and flood checks. |
| Windows official notification collector | 5 | Collector compiles; MSIX identity/capability and interactive consent flow are absent. Opt-in API works instead. |
| Quick Action registry and authorization | 2 | Registry transmitted in Host integration; local API rejects unlisted actions. |
| Configured launch and microphone-mute actions | 3 | Native Windows implementations compile; no application launch or microphone state change used as an acceptance test. |
| Switch default Windows audio output | 7 | No supported endpoint-switch implementation selected. |
| Native UIKit dynamic media/actions/worker UI | 3 | ARM64/iOS 12 app compiled, linked, ad-hoc signed and packaged; no device layout, gestures, artwork, responsiveness or visual verification. |
| Native fullscreen/banner/history presentation | 3 | Compiled/analyzed expiry/auto-return and bounded history. Never shown on phone. |
| SHA-256, integrity, metadata, text and named diagnostics worker | 2; native 3 | Simulator streamed operations/recovery pass. Native worker compiles/analyzes but has no device execution. |
| Binary file upload/download, checksums and resume | 2; native 3 | Simulator socket upload/checksum/resume/range tests pass. Native file writes/fsync moved to bounded utility queue; replies generation-fenced. Native timing and correctness remain unverified. |
| Archive backup | 5 | Opaque archive storage/transfer implemented; directory snapshot creation, retention policy and block-delta incremental backups absent. |
| SSH diagnostics and own-file deployment | 1 — actual iPhone | User-authorized root SSH and SCP via USB port 44 succeeded with strict isolated host-key verification. Port 22 refused. Windows product's key-based SSH utility remains compiled-only; no credential embedded. |
| GhostCode SubmitJob/CancelJob/status/archive/artifact/events/progress API | 2 | Host API/auth and worker protocol tested; packaged-process smoke test verifies SSE, archive/checksum/artifact round trip and recovery after an agent-process restart. No GhostCode installation or repository/database access required. |
| Handoff abstraction and URL validation | 2 | Connectivity/direct-HTTPS/YouTube rejection tests pass. |
| Native direct AVPlayer handoff | 5 | Source receipt/play invocation only, no successful playback/result handshake or coordinated PCM pause/resume. |
| Independent YouTube playback/offload | 7 | No provider extraction or unrestricted background YouTube support. Modes A/B remain the reliable architecture. |
| Background worker independent of UI termination | 5 | Separate manually started Agent packaged, no launchd service. It was killed before main; background/audio privileges and worker independence remain unverified. |
| Memory-pressure scheduling | 2; native 3 | Simulator pause/reject/cancel tests pass. Native warning/cache handling compiles/analyzes; no actual device pressure test. |
| Actual iPhone RSS/Jetsam/thermal/battery measurements | 6 | App cannot execute. Never disabled Jetsam or changed phone services. |
| Windows tray | 3 | WinForms app compiles and publishes; native interactive tray UI has not been manually inspected. |
| Windows portable packaging | 1 — Windows command-line processes | Self-contained Host and simulator run without the system .NET 8 runtime; real packaged-process API, SSE, file and restart checks pass. Tray UI remains category 3. |
| iOS ARM64 build/sign/package | 3 — artifacts verified | Seven native units compile/analyze cleanly; UI/Agent link as iOS 12 ARM64. Two deterministic Debian packages plus IPA; eight artifact tests pass. Ad-hoc signatures have valid local page/resource hashes, not device signing authorization. |
| iOS installation/execution | 6 — actual failures recorded | SSH-staged Agent/probe killed by System Policy before main; app-scoped uicache rejected the writable staging location. Standard Installation Proxy reached signature verification and returned ApplicationVerificationFailed / 0xe8008014. Own temporary staging removed. |
| iPhone diagnostic log collection | 1 — actual iPhone | Syslog relay available and bounded BackThread-only collection run; kernel execution-policy denial inspected over SSH. No native runtime/crash/audio/memory log exists. |

The follow-up preserved Windows Host/simulator product source and passed 19/19 regular managed tests plus shared C tests. The older checkpoint's 120-second simulator soak remains historical evidence; no new long soak is claimed. See [native audit](IOS_NATIVE_AUDIT.md) and [hardware results](HARDWARE_TEST_RESULTS.md). Final end-to-end hardware acceptance has **not** been achieved.
