# Security boundary and review

BackThread is a local user-session application, not a privileged device-management tool. Device preparation and system security changes are outside its scope.

## Implemented controls

- Both the simulator and iOS application bind to 127.0.0.1 only. The Host connects to Apple usbmux on Windows loopback or a development loopback socket. Wi-Fi device entries are excluded.
- Mutual HMAC-SHA256 challenge/response uses fresh 256-bit nonces, domain-separated roles and constant-time proof comparison. The key is generated from the OS CSPRNG; no default key is shipped. Wrong-key and replay rejection are socket-tested.
- The HTTP API binds to 127.0.0.1, requires a bearer secret, rejects Origin headers, enables no CORS, caps request bodies at 64 KiB, limits active requests/connections/subscribers, and uses Kestrel's parser. No password or token is logged.
- Frame version/channel/size checks precede payload allocation. Audio/file/control limits are separate. JSON envelopes and storage names are validated; protocol failures close a connection. Request deadlines and bounded queues prevent ordinary unlimited buffering.
- Files remain under application storage. Basenames reject separators, traversal, hidden names, colons and Windows device names. Complete SHA-256 verification precedes atomic replacement. Archives are stored as opaque files, never executed or extracted. New transfers reserve their full declared length against a default 2 GiB application quota, with at most 256 completed files and 16 partial transfers; admission also requires 64 MiB of free-space headroom.
- Remote jobs are named operations, not shell input. Quick Actions use locally configured IDs and fixed local executable paths/argument arrays. No remote arguments are appended to a shell command.
- The product SSH utility uses installed OpenSSH with BatchMode, strict host-key checking, explicit identity files and a loopback port, with only identity/storage/uptime presets. Device integration separately used the user's credential interactively through USB port 44 and an isolated verified host record, and staged only BackThread-owned files. Standard paired AFC/Installation Proxy installation was attempted with certificate pinning and normal signature verification; the device rejected it. No password embedding, security-policy changes or automatic host-key enrollment was added.
- Sensitive notifications are masked by default, event sources can be blocked, history is bounded and events expire. Notification collection is optional and requires permission already granted through an appropriate Windows package/UI flow.
- Keys, local settings, binaries, partial files and test state are git-ignored. iOS provisioning stores its key with mode 0600 and protection until first unlock. No phone UDID, private pairing record, password or generated secret is shipped in the source or reports.

## Threat-model limits

Traffic is not encrypted and frames do not have individual message authentication codes. A compromised Windows process, injected phone process, local administrator, or compromised USB/service path is outside the current trust boundary. The bearer key intentionally grants API access to local files named in SubmitArchive requests; do not give it to untrusted integrations. It is not a sandbox credential.

A copied key authorizes the holder. Key rotation currently means replacing both key files and restarting Host and Agent. There is no per-application access policy, keychain enrollment flow, revocation UI, public-network mode or transport TLS implementation.

Quota admission counts both existing complete files and declared partial-transfer reservations. Replacement uploads need room for the old and new versions until commit. Other applications can consume device space after admission, so disk-write errors still propagate. Partial files remain until completed or manually removed within the app's own data directory; automatic cleanup and retention are not implemented. The native quota code compiles and analyzes cleanly, but its runtime enforcement is unverified.

Completed files use replace semantics. Callers must choose unique backup names when preserving previous versions. Job idempotency is bounded by the sixty-four-record history; it is not an unlimited exactly-once database. Power-loss durability is not guaranteed by managed flush operations. A local pairing record is not proof that the current device is trusted or jailbroken.

Native code compiles for iOS 12 ARM64 and has executed through the embedded Agent on the recorded physical phone. Current recovery repairs are compiled/analyzed but not physically verified. Static review generation-fences delayed commands/file responses against descriptor reuse; this does not establish complete race freedom or fuzz resistance. User-prepared normal installation succeeded; standalone execution policy remained unchanged and denied. No BackThread repair of missing system/jailbreak components is authorized. Current hardware state is unknown after the user's black-screen maintenance.

## Checks performed

Managed tests exercise malformed/oversized frames, fragmented reads, wrong keys, replayed authentication, socket recovery, unsafe paths, checksum/offset errors, unsupported jobs/formats, cancellation, queue saturation, privacy masking, API authorization, Origin rejection, and reconnects. Shared C framing/ring code compiles with warnings as errors. NuGet advisory output is preserved with the reports. Passing these checks is not a complete penetration test.

## 0.1.2 deployment and transport review

The user's prepared AppSync/dpkg environment was inspected read only. BackThread initiated only normal installation/upgrade/uninstall of its own UI/probes and scoped file staging; no jailbreak package, activation, firmware, trust cache, AMFI, sandbox or unrelated service policy was changed. A normal dpkg probe removal processed the package manager's existing Cydia trigger; no unrelated package was installed/removed or edited.

Existing apps use Installation Proxy Upgrade, not a fresh Install. A physical sentinel test verifies data preservation while the container path rotates; callers must refresh own app metadata. The installer only accepts BackThread's fixed UI/probe identifiers. Explicit uninstall removes only the selected app/container; package removal is scoped to the named BackThread package. No automatic broad data cleanup is performed.

Debian archives now contain only explicit app-owned directory roots, not shared parent metadata. Agent directories use target mobile UID/GID 501 and private mode 0700 at the data root; /var/mobile ownership is not packaged. Executables/resources contain no generated key or SDK/framework binary. Own probes were installed/removed during separate diagnostic stages; the last recorded UI used a private matching key provisioned only when absent. Current app/key/probe presence must be rechecked after readiness rather than assumed.

Launch-only URL schemes open the apps but do not interpret arguments as actions, jobs or key material. Device syslog records support NUL/LF framing, bounded retention/partial lines and UUID redaction; only BackThread matches are retained. Entry markers, PIDs, installer completion and actual application behavior remain separate evidence.

Windows artifact responses now validate offset, requested length and declared reply length, with an atomic four-download limit. A real-socket fault test rejects corrupt data/metadata and recovers on a valid response. Native file reads/writes use bounded utility work and generation-fenced replies; basic native audio/file concurrency was exercised; extended failure/restart behavior is still unverified. No new production dependency or wire version was introduced.

## Device boundary during repairs

After the black-screen report all repair work is offline. Existing connection, certificate and app metadata may only be rechecked when the user reports readiness. New installation remains restricted to BackThread-owned payloads through already available authorized methods. Do not repair or install missing frameworks, daemons, drivers, jailbreak/activation/bypass components, or alter firmware/AMFI/trust caches/Jetsam. If these are unavailable, document the blocker and continue independent repository work. No password/key/pairing data is included in releases.
