# BackThread protocol v1

## Framing

Every frame is a 24-byte little-endian header followed by exactly `length` bytes. Implementations must reject the header before allocating a payload if any limit or version check fails. Partial TCP reads are normal; EOF within a header or body closes the session.

| Offset | Type | Meaning |
|---|---|---|
| 0 | uint32 | Payload length, excluding header |
| 4 | uint32 | Magic `0x31485442`, bytes `BTH1` |
| 8 | uint16 | Version, exactly 1 |
| 10 | uint16 | Logical channel 1–7 |
| 12 | uint32 | Per-channel sequence counter, wraps modulo 2^32 |
| 16 | uint64 | Audio source sample time in microseconds; zero for ordinary control |

| Channel | ID | Maximum payload |
|---|---|---|
| Control | 1 | 65,536 bytes |
| Audio | 2 | 8,192 bytes |
| Media | 3 | 65,536 bytes |
| Notifications | 4 | 65,536 bytes |
| Jobs | 5 | 65,536 bytes |
| Files | 6 | 32,808 bytes |
| Diagnostics | 7 | 65,536 bytes |

Other than channels 2 and 6, payloads are UTF-8 JSON objects: `{"type":"hello","id":"optional request ID","replyTo":null,"body":{}}`. Type is required and at most 80 characters. IDs are at most 64 characters. JSON body must be an object; .NET limits JSON nesting to 16. Responses use `type=ok` and `replyTo=<request id>`, or `type=error` with `body.error`. Pong uses `type=pong`. Unsolicited progress and media states omit `replyTo`. Keep all application messages inside the frame limit, including base64 artwork.

## Authentication

No application commands or PCM are permitted before authentication. Provision the same random 32-byte key to Host and Agent out of band. Files store it as exactly 64 hexadecimal characters.

1. Agent sends channel 1 `auth.challenge` with a cryptographically random lowercase 64-character `serverNonce`.
2. Host chooses a new random lowercase 64-character `clientNonce` and sends `auth.response` with that nonce and a lowercase hex SHA-256 HMAC proof.
3. Agent verifies proof in constant time, sends `auth.ok` with its own proof and `protocol=1`. Host verifies the agent proof before using the connection.

The HMAC input is ASCII `BTH1:<role>:<serverNonce>:<clientNonce>`, where role is `host` or `agent`. The nonces in this string are their hex text, not decoded bytes. No nonce or proof is reused on reconnect. Authentication times out; failures close the connection. Traffic is authenticated at session establishment but is **not encrypted or individually MACed**. Only loopback and the trusted USB path are supported.

## Control, media and events

- `hello {host,protocol}` → `{agent,simulated,capabilities}`.
- `ping {}` → pong. Host sends every three seconds; deadline five seconds. General reads expire after twelve seconds.
- `audio.start {sampleRate:48000,channels:2,bits:16,packetMs:10}` → `{ready:true}`. Other formats are explicitly rejected. `audio.stop {}` clears playback.
- Audio frames contain raw PCM, complete four-byte stereo samples; typical payload is 1,920 bytes. Header timestamp starts at zero for each negotiated stream. Gaps can result from intentional bounded queue eviction.
- `audio.stats {}` reports packet/byte/gap and ring counters. Simulator additionally reports a received PCM SHA-256. Native iOS additionally reports callbacks, lastOSStatus, running/primed/interrupted flags, routeTypes and outputVolume. These are runtime counters only when a real process responds; they do not establish acoustic output or human audibility.
- `audio.volume {value:0..1}` controls the iOS audio queue gain. The visible native MPVolumeView controls supported device volume/routes.
- `media.state {session:<metadata or null>}` carries source, title, artist, album, state, positionSeconds, durationSeconds, canSeek, canPlay/canPause/canNext/canPrevious and optional artworkBase64.
- Device `media.command {command,seconds?,source?}` asks the Host to play, pause, toggle, next, previous or seek. Host returns actual API acceptance. Windows applications determine available commands.
- `action.registry {actions:[{id,title}]}` advertises locally configured action IDs. Device sends `action.invoke {actionId}`; arbitrary commands are forbidden.
- `event.publish {id,source,title,body,priority,expiresUtc,sensitive}` → `{accepted}`. Priorities 0–3 are bounded history only, banner, interruption, high interruption. Fullscreen presentations auto-return. `event.history {}` returns retained events; callers must still honor expiry.
- `media.handoff {url}` is the experimental direct-HTTPS provider boundary. Receipt is not confirmation of successful playback.

## Files

1. `file.begin {transferId,name,size,sha256}` → `{offset}`. Transfer ID is 32 hex characters, size is 0 through 2 GiB, SHA-256 is 64 hex characters. Repeat begin must match all original fields. New uploads reserve size against the default 2 GiB total app quota (complete files plus declared partial uploads); admission requires 64 MiB free-space headroom. Up to 256 complete files and sixteen partial uploads are allowed.
2. Channel 6 chunk: first 32 bytes are ASCII transfer ID; next 8 are signed nonnegative little-endian offset; remaining 0–32,768 bytes are binary file content. Only contiguous append or completely repeated chunks are accepted.
3. Receiver flushes and sends channel 5 `file.ack {transferId,offset}`. Sender permits one unacknowledged upload chunk, times out after ten seconds, and resumes with begin after reconnect.
4. `file.commit {transferId}` verifies declared length and complete SHA-256, atomically replaces the basename destination, and returns `{name,size,sha256}`. A mismatch retains partial data and produces an error. Commit may take up to five minutes; it runs outside the network read loop.

`file.list {}` returns up to 256 names/sizes. `file.read {name,offset,count,transferId}` returns a channel 6 chunk with that ID and offset, plus an ordinary success reply containing the length. Download count is 1–32,768. The Host validates the binary prefix offset, received size against the requested count, and the declared reply length against the bytes received; corrupt responses fail the request. At most four Host downloads are active. Native reads use the bounded utility I/O queue and generation-fenced frames/replies; a busy read reports an error. Zero-length data means EOF. Requests cannot read outside application storage.

The stable upload ID is the first 32 lowercase hex characters of SHA-256 over UTF-8 `<storage name>:<whole-file lowercase sha256>`. A fresh completed upload may be sent again; an incomplete one resumes. There is no transactional multi-file snapshot or block-delta algorithm.

## Jobs

`job.submit {jobId,kind,name,expectedSha256?}`, `job.query {jobId}` and `job.cancel {jobId}` operate on bounded state. Kinds are `sha256`, `verify`, `metadata`, `text`, and `diagnostic`. Diagnostic names are explicitly allowlisted, never interpreted as a shell command. Text returns byte, line and word counts; iOS uses ASCII whitespace and .NET uses Unicode whitespace, a documented portability limitation.

State is `{jobId,state,progress,result?,error?}`. States: queued, running, completed, failed, cancelled, interrupted. Progress is 0–1 and advisory. `job.progress` pushes updates. Duplicate IDs return existing state; a conflicting spec during the same process lifetime is rejected. Historical records do not persist full arguments, so after restart a duplicate ID means query, not resubmit; use a new ID for retries.

## Simulator-only controls

`fault.disconnect {}` deliberately closes a session. `memory.pressure {active}` pauses or resumes optional worker processing. CLI `--delay-ms` delays message processing; `--disconnect-after` closes after a specified audio packet count. These capabilities are absent from native production dispatch. Restart the simulator process with the same state directory to test persistent recovery.

## Audio failure and diagnostic fields (0.1.2 repair)

An unsolicited `audio.error` on Diagnostics includes a bounded error string and lastOSStatus after a running queue fails. It is advisory: framing version and authentication are unchanged. The native receiver stops that queue and discards pending PCM while keeping ping/media/jobs/files on the same session. Explicit successful audio.start re-enables PCM. Malformed framing still closes the session. audio.volume now returns error on invalid values or AudioQueue parameter failure.

audio.stats adds acceptingPCM, queuePresent, queueRunningStatus and queueGainStatus beside queueRunning/gain, route/category/mode and input/render peaks/nonzero sample counts. Getter success is separate from the last queue-operation OSStatus. These fields describe APIs and buffers, not acoustic output. Host /v1/status exposes the last unsolicited receiver failure as audioError; a new authenticated Host session clears it.

Windows frame encoding is unchanged, but header and payload are now written together using one bounded pooled buffer retained until WriteAsync completes. Host reports audioWriteMaximumMilliseconds/audioSlowWrites (>=50ms): wall time awaiting audio writes, which includes local scheduling and is not an isolated USB latency measurement. A byte-exact native-compatible golden header and delayed-write ownership regression cover this path. Physical stutter improvement from this Windows change remains unverified after the user unplugged the phone.
