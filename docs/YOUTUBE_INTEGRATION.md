# YouTube Integration & Strict Compliance Boundary

BackThread handles `youtube.com`, `youtu.be`, and `music.youtube.com` URLs strictly through official metadata APIs and Windows remote playback coordination—never through reverse-engineered extractors, signature deciphering, DRM bypass, or background WebView workarounds.

## Strict Prohibitions Enforced in Source

1. **Zero HTML scraping or stream extraction**: BackThread never fetches/parses YouTube watch-page HTML, player JSON scripts, `Decipher` JS logic, PO tokens, SABR formats, or third-party extractor code (zero GPL `youTFast` or `yt-dlp` code is included or invoked).
2. **Zero unauthorized background playback**: [`YouTubeProvider.BuildCapabilities`](../src/Windows/Media/YouTubeProvider.cs) explicitly reports `BackgroundSupported = false` and `SupportsDirectInternet = false` for web URLs.
3. **Zero CORS/Origin weakening**: The Chromium Native Messaging workflow uses explicit Windows local `Host` CLI communication, never cross-origin HTTP calls.

## Supported YouTube Workflows

### 1. Default Workflow: Mode D Remote Control + Mode C Live USB PCM
- [`YouTubeProvider.TryParseVideoId`](../src/Windows/Media/YouTubeProvider.cs) validates canonical URLs (`watch?v=`, `youtu.be/<id>`, `shorts/<id>`, `live/<id>`, `embed/<id>`) plus `t=`/`start=` offsets.
- [`YouTubeProvider.FetchMetadataAsync`](../src/Windows/Media/YouTubeProvider.cs) calls YouTube's official unauthenticated oEmbed endpoint (`https://www.youtube.com/oembed?url=...&format=json`) with clean offline fallback metadata.
- Playback runs in the user's Windows browser/YouTube desktop session; **GSMTC (`Mode D`)** surfaces Play/Pause/Seek/Title on the iPhone's `BTNowPlayingView`, while **WASAPI (`Mode C`)** optionally streams Windows audio over USB.

### 2. Opt-In Experimental Official Embed (`allowExperimentalYouTubeEmbed: true`)
- Off by default (`config/example.json`: `"allowExperimentalYouTubeEmbed": false`).
- When explicitly opted in, [`MediaRouter`](../src/Windows/Media/MediaRouter.cs) attaches `experimentalEmbed: true` with the canonical `https://www.youtube-nocookie.com/embed/<id>?playsinline=1&rel=0` URL.
- iOS [`BTYouTubeEmbedController`](../src/iOS/Media/BTYouTubeEmbedController.m) renders the embed inside an isolated `WKWebView` restricted by `WKNavigationDelegate` only to `youtube-nocookie.com`, `youtube.com`, and `googlevideo.com` subresources, showing explicit controls: **Return to Windows** and **Studio**.
- If the creator disables embedding or navigation fails, or the user taps Return, `BTYouTubeEmbedController` invokes `onFallbackRequested`, cleanly closing the embed, emitting `media.handoff.status {stage:"returned", fallbackSuggested:true}`, and resuming Windows **Mode D + Live PCM**.

## Verification Boundaries

- **Verified in Unit Tests**: Canonical URL parsing (`youtu.be`, `watch?v=`, `shorts`, `live`, `t=1h2m3s`), oEmbed JSON parsing, capability limitation payloads, and clean Mode D/Live PCM fallback (`media.prober-and-youtube-oembed-adapter` in `HybridMediaTests.cs`), plus Clang static analysis and ARM64 iOS 12 compilation of `BTYouTubeEmbedController.m`.
- **Unverified on Hardware**: Live online `WKWebView` playback on physical iPhone 6 / iOS 12.5.8 WebKit remains pending physical testing.
