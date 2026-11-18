# Security Boundary and Review

BackThread is a local user-session Windows-to-iPhone companion system, not a privileged device-management tool or remote access service. Device preparation, jailbreak tooling, and OS policy configuration remain outside BackThread's scope.

## Implemented Controls

- **Loopback & USB Isolation**: `BTAgent` and `Simulator` bind strictly to `127.0.0.1:23451`. Windows connects only via Apple `usbmux` USB entries (Wi-Fi usbmux entries are explicitly excluded) or configured `127.0.0.1` simulator ports.
- **Mutual Session Authentication**: 256-bit CSPRNG nonces and domain-separated HMAC-SHA256 challenge/response (`BTH1:host:` / `BTH1:agent:`) with constant-time comparison guard every TCP session.
- **Loopback API & Extension Hygiene**:
  - `LocalApi` binds only to `127.0.0.1:23452`, enforces fixed-time Bearer key checks, caps bodies at 64 KiB, bounds concurrency (`8` requests / `16` connections / `4` SSE listeners), and rejects any request with an `Origin` header (`401`).
  - `/v1/spotify/callback` permits only `GET` with CSPRNG single-use PKCE `state` verification (`403` on mismatch) and reveals no internal API data.
  - The Chromium MV3 extension (`extensions/chromium/`) requests ONLY `activeTab` and `nativeMessaging` permissions; it never inspects browsing history, page cookies, DOM credentials, or Bearer keys directly in JS (`BackThread.Host.exe --native-messaging` executes locally as a desktop host process).
- **Spotify Token Protection**: PKCE OAuth 2.0 eliminates client secrets; [`LocalSpotifyTokenStore`](../src/Windows/Media/SpotifyProvider.cs) encrypts tokens at rest on Windows with `CryptProtectData` (`DPAPI`, `CurrentUser`).
- **URL Probing & Anti-Bypass Guarantees**: [`MediaCapabilityProber`](../src/Windows/Media/Handoff.cs) permits only `https://` in production Mode A, rejects localhost/link-local/RFC1918 IP targets against SSRF, rejects generic `text/html` pages from `AVPlayer`, and never scraps HTML, deciphers YouTube signatures, strips DRM, or installs system tweaks. Opt-in `BTYouTubeEmbedController` (`allowExperimentalYouTubeEmbed: true`) uses an isolated `WKWebView` domain allowlist (`youtube-nocookie.com`, `youtube.com`, `googlevideo.com`) with no JS injection tricks.
- **File, Buffer & Action Bounds**: Storage rejects path separators, traversal, hidden files, colons, and Windows device names (`2 GiB` quota, `64 MiB` free headroom, `256` files, `16` partial uploads). Mode B `BTBufferedMediaEngine` enforces strict stream ID / generation fencing and a `24 MiB` LRU RAM cap. Quick Actions accept only locally configured allowlisted IDs—never remote shell input or paths.
- **Notification Privacy**: `hideSensitive: true` strips body, custom title, app icon (`<=24 KiB`), and actions on sensitive events before Channel 4 transmission; blocked sources drop before retention; expired items purge from the 64-entry history.

## Threat-Model Boundaries

- Traffic over USB (`BTH1`) relies on physical lightning-cable/local-host trust and is authenticated per session via HMAC handshake, not TLS-encrypted or individually per-packet MACed.
- No session keys, client secrets, tokens, or pairing keys are stored in source control (`*.key`, `*.secret`, `local.json` are git-ignored) or staged by `publish-windows-release.py` (enforced programmatically before `checksums.sha256` output).
- Physical hardware security behavior (`NSFileProtectionCompleteUntilFirstUserAuthentication` lock screen edge cases, Windows DPAPI cross-user tests, live MSIX capability prompt) remains bounded by hardware verification rules in [FEATURE_STATUS.md](FEATURE_STATUS.md) and [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
