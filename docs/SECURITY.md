# Security boundary and review

BackThread is a local user-session application, not a privileged device-management tool. Device preparation and system security changes are outside its scope.

## Implemented controls

- Both the simulator and iOS application bind to 127.0.0.1 only. The Host connects to Apple usbmux on Windows loopback or a development loopback socket. Wi-Fi device entries are excluded.
- Mutual HMAC-SHA256 challenge/response uses fresh 256-bit nonces, domain-separated roles and constant-time proof comparison. The key is generated from the OS CSPRNG; no default key is shipped. Wrong-key and replay rejection are socket-tested.
- The HTTP API binds to 127.0.0.1, requires a bearer secret, rejects Origin headers, enables no CORS, caps request bodies at 64 KiB, limits active requests/connections/subscribers, and uses Kestrel's parser. No password or token is logged.
- Frame version/channel/size checks precede payload allocation. Audio/file/control limits are separate. JSON envelopes and storage names are validated; protocol failures close a connection. Request deadlines and bounded queues prevent ordinary unlimited buffering.
- Files remain under application storage. Basenames reject separators, traversal, hidden names, colons and Windows device names. Complete SHA-256 verification precedes atomic replacement. Archives are stored as opaque files, never executed or extracted. New transfers reserve their full declared length against a default 2 GiB application quota, with at most 256 completed files and 16 partial transfers; admission also requires 64 MiB of free-space headroom.
- Remote jobs are named operations, not shell input. Quick Actions use locally configured IDs and fixed local executable paths/argument arrays. No remote arguments are appended to a shell command.
- SSH uses the installed OpenSSH client with BatchMode, strict host-key checking, explicit identity files and a configured loopback port. Only identity, storage and uptime diagnostic presets exist. There is no password embedding, automatic host-key enrollment or remote installation.
- Sensitive notifications are masked by default, event sources can be blocked, history is bounded and events expire. Notification collection is optional and requires permission already granted through an appropriate Windows package/UI flow.
- Keys, local settings, binaries, partial files and test state are git-ignored. iOS provisioning stores its key with mode 0600 and protection until first unlock. No phone UDID, private pairing record, password or generated secret is shipped in the source or reports.

## Threat-model limits

Traffic is not encrypted and frames do not have individual message authentication codes. A compromised Windows process, injected phone process, local administrator, or compromised USB/service path is outside the current trust boundary. The bearer key intentionally grants API access to local files named in SubmitArchive requests; do not give it to untrusted integrations. It is not a sandbox credential.

A copied key authorizes the holder. Key rotation currently means replacing both key files and restarting Host and Agent. There is no per-application access policy, keychain enrollment flow, revocation UI, public-network mode or transport TLS implementation.

Quota admission counts both existing complete files and declared partial-transfer reservations. Replacement uploads need room for the old and new versions until commit. Other applications can consume device space after admission, so disk-write errors still propagate. Partial files remain until completed or manually removed within the app's own data directory; automatic cleanup and retention are not implemented. The native quota code remains uncompiled, like the rest of the Objective-C agent.

Completed files use replace semantics. Callers must choose unique backup names when preserving previous versions. Job idempotency is bounded by the sixty-four-record history; it is not an unlimited exactly-once database. Power-loss durability is not guaranteed by managed flush operations. A local pairing record is not proof that the current device is trusted or jailbroken.

The native Objective-C sources have not been compiled or runtime-tested. Static review found and corrected an authentication-boundary issue: asynchronous job progress must not reach a newly accepted socket before that socket authenticates. More device-side review, fuzzing and lifecycle validation are required before treating this as a hardened release.

## Checks performed

Managed tests exercise malformed/oversized frames, fragmented reads, wrong keys, replayed authentication, socket recovery, unsafe paths, checksum/offset errors, unsupported jobs/formats, cancellation, queue saturation, privacy masking, API authorization, Origin rejection, and reconnects. Shared C framing/ring code compiles with warnings as errors. NuGet advisory output is preserved with the reports. Passing these checks is not a complete penetration test.
