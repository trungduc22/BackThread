# BackThread Wire Protocol v1 (`BTH1`)

## Framing

Every frame begins with a 24-byte little-endian header followed by `length` payload bytes (`bt_wire.h` / `Protocol.cs`):

| Offset | Type | Meaning |
|---|---|---|
| 0 | `uint32` | Payload length excluding header |
| 4 | `uint32` | Magic `0x31485442` (`BTH1`) |
| 8 | `uint16` | Version (`1`) |
| 10 | `uint16` | Logical channel `1–7` |
| 12 | `uint32` | Per-channel wrapping sequence counter |
| 16 | `uint64` | Audio source sample timestamp in microseconds (`0` for non-audio frames) |

| Channel | ID | Max Payload | Purpose |
|---|---|---|---|
| Control | 1 | 65,536 B | Auth, `hello`, `ping`/`pong`, `action.*` |
| Audio | 2 | 8,192 B | Raw 48 kHz 16-bit interleaved stereo PCM (`1,920 B` typical per 10 ms frame) |
| Media | 3 | 65,536 B | Hybrid media handoff, focus, sync, commands, Mode B metadata |
| Notifications | 4 | 65,536 B | Rich companion events (`event.publish`, `event.history`) |
| Jobs | 5 | 65,536 B | `job.submit/query/cancel/progress`, `file.begin/commit/list/read`, `file.ack` |
| Files | 6 | 32,808 B | Binary chunks: `32-char ASCII transferId` (or `streamId` for Mode B) + `uint64` offset + payload (or header-prefixed Mode B frames) |
| Diagnostics | 7 | 65,536 B | `audio.stats`, unsolicited `audio.error` |

JSON payloads carry UTF-8 objects `{type, id?, replyTo?, body}` (max nesting depth 16, `type` <= 80 chars, `id` <= 64 chars).

## Mutual Authentication

No Channel 2–7 traffic or application actions are processed before mutual authentication completes:
1. Agent sends `auth.challenge {serverNonce}` (Channel 1, 64 lowercase hex CSPRNG chars).
2. Host returns `auth.response {clientNonce, proof}` where `proof = HMAC-SHA256(keyBytes, "BTH1:host:<serverNonce>:<clientNonce>")`.
3. Agent verifies in constant time and replies `auth.ok {proof, protocol:1}` with `"BTH1:agent:<serverNonce>:<clientNonce>"`.

## Channel 1 & 7 — Control & Audio Diagnostics

- `hello {host, protocol}` -> `{agent, simulated, capabilities}`
- `ping {}` -> `pong {}` (every 3s; 5s deadline; general read idle timeout 12s)
- `audio.start {sampleRate:48000, channels:2, bits:16, packetMs:10}` / `audio.stop {}` / `audio.volume {value:0..1}`
- `audio.stats {}`: Returns receiver packets/bytes/gaps/bufferedBytes/droppedBytes/silenceBytes, queue running/primed/interrupted flags, route/outputVolume, `lastOSStatus`, `queuePresent`, `acceptingPCM`, input/render peaks.
- Unsolicited `audio.error`: Non-fatal queue failure alert; preserves session for Ping, Media, Events, and Jobs.
- `action.registry {actions:[{id, title}]}` and `action.invoke {actionId}` (allowlisted IDs only).

## Channel 3 — Hybrid Media & External PiP Protocol (0.2.0)

- `media.state {mode, owner, generation, provider, session}`: Windows host GSMTC/Spotify/router state presentation (`<=24 KiB` thumbnail artwork).
- `media.handoff {sessionId, generation, mode, url, title, artist, mimeType, qualityCap, keepScreenAwake, startPositionSeconds, experimentalEmbed?}`: Host requests independent playback on iPhone (Mode A HTTPS/HLS or opt-in `youtube-nocookie` embed).
- `media.handoff.status {sessionId, generation, stage, positionSeconds, durationSeconds, bufferedSeconds, isPlaying, readyForDisplay, videoWidth, videoHeight, error?, reason?, fallbackSuggested?}`: Reported by iPhone (`validating`, `connecting`, `buffering`, `confirmed`, `failed`, `returned`). **Host pauses Windows ONLY when `stage == "confirmed"` matches current generation.**
- `media.player.state {sessionId, generation, isPlaying, positionSeconds, durationSeconds, bufferedSeconds, readyForDisplay, videoWidth, videoHeight}`: Periodic/seek/play/pause progress updates from iOS `BTDirectMediaPlayer`.
- `media.buffered.start {streamId, generation, totalBytes, mimeType, title, artist, qualityCap, keepScreenAwake}` & `media.buffered.eof {streamId}`: Mode B initialization and end-of-stream signals; iOS requests ranges via `media.buffered.request {streamId, generation, offset, length}` -> Windows streams Ch 6 chunks (`streamId` + offset + bytes).
- `media.handoff.cancel {sessionId, generation, reason}`: Cancels pending handoff.
- `media.return_to_windows {resumeWindows, positionSeconds?}`: Bidirectional handoff return initiated by Windows UI or iOS user button.
- `media.focus.set {policy, muteVideoAudio?, duckVideo?, volume?}`: Bidirectional audio focus arbitration (`Auto`, `IndependentOnly`, `LivePcmOnly`, `KeepVideoMuteAudio`, `DuckVideo`).
- `media.mode.select {mode, provider?}`: Notifies mode badge transition cleanly.
- `media.command {command, seconds?, qualityCap?, keepScreenAwake?, source?}`: Controls active owner (`play`, `pause`, `toggle`, `next`, `previous`, `seek`, `quality`, `keepAwake`).
- `media.sync {generation, owner, mode}` -> `{sessionId, generation, mode, independentActive, isPlaying, positionSeconds, durationSeconds, bufferedSeconds, url, title}`: Reconciles Wi-Fi independent PiP state after USB reconnect.

## Channel 4, 5 & 6 — Rich Events, Worker & Files

- `event.publish {id, source, title, body, priority, expiresUtc, sensitive, appName?, category?, iconBase64?, actions?}` -> `{accepted}`
- `file.begin {transferId, name, size, sha256}`, binary Channel 6 chunk append + Channel 5 `file.ack`, `file.commit`, `file.list`, `file.read`, `job.submit`, `job.query`, `job.cancel`, `job.progress` — unchanged bounded quota/integrity contracts.
