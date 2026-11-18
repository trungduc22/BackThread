# Media Provider Compatibility Matrix

BackThread evaluates every media URL, file path, or active Windows session strictly through [`MediaCapabilityProber`](../src/Windows/Media/Handoff.cs), [`DirectMediaProvider`](../src/Windows/Media/Handoff.cs), [`YouTubeProvider`](../src/Windows/Media/YouTubeProvider.cs), [`SpotifyProvider`](../src/Windows/Media/SpotifyProvider.cs), and [`BufferedMediaSource`](../src/Windows/Media/BufferedMediaSource.cs). Unsupported or DRM-protected sources fail safely onto Mode D + Mode C without scraping or license/DRM bypasses.

| Source / Content | Mode Selected | iPhone Behavior | Windows Behavior | Survives USB Disconnect? | Limitations & Compliance Notes |
|---|---|---|---|---|---|
| **Direct HTTPS Video** (`.mp4`, `.m4v`, `.mov`) | **Mode A** (`DirectInternet`) | Hardware `AVPlayer` over iPhone Wi-Fi; fullscreen PiP + Ambient | Auto-paused once after `confirmed` | **Yes** (Wi-Fi) | Requires `deviceHasInternet: true` & public/reachable HTTPS; SSRF private/loopback IPs blocked unless simulator override |
| **Direct HLS Stream** (`.m3u8`) | **Mode A** (`DirectInternet`) | `AVPlayer` adaptive HLS over Wi-Fi (`preferredPeakBitRate` caps apply) | Auto-paused after `confirmed` | **Yes** (Wi-Fi) | DRM/FairPlay encrypted playlists fail cleanly back to Windows |
| **Direct HTTPS Audio** (`.mp3`, `.m4a`, `.aac`) | **Mode A** (`DirectInternet`) | Background-capable `AVAudioSession` playback in `BTNowPlayingView` | Auto-paused after `confirmed` | **Yes** (Wi-Fi) | Keeps `BTNowPlayingView` visible (does not force video focus mode) |
| **Local Windows Video / Audio File** (`.mp4`, `.m4v`, `.mov`, `.mp3`, `.m4a`) | **Mode B** (`BufferedUsb`) | Incremental USB buffered playback via `BTBufferedMediaEngine` (`24 MiB` LRU) | Reads local file chunks (max 4 GiB validated; chunked 256 KiB) | **Partially** (plays already-buffered window) | Requires USB cable; does not persist files outside bounded cache |
| **Spotify Desktop / Spotify Web** | **Mode D** (`RemoteControl`) + **Mode C** (`LivePcm`) | `BTNowPlayingView` artwork, play/pause/next/prev/seek + USB speaker audio | Spotify Web API Connect control (PKCE + DPAPI) or GSMTC + WASAPI | **No** (control & PCM over USB) | **No DRM/stream stripping**; Spotify audio always originates on Windows/Connect devices |
| **YouTube / YouTube Music (Default)** | **Mode D** (`RemoteControl`) + **Mode C** (`LivePcm`) | oEmbed title/author metadata + GSMTC transport + USB Live PCM | Plays in Windows browser + GSMTC + WASAPI loopback | **No** | **Zero HTML scraping / cipher decoding / background WebView bypass** |
| **YouTube (Opt-In `allowExperimentalYouTubeEmbed: true`)** | **Mode A Embed** (`youtube-nocookie.com`) | Isolated `WKWebView` official nocookie embed (`BTYouTubeEmbedController`) | Paused if embed confirms; fallback button restores Mode D + PCM | **While foregrounded on Wi-Fi** | Embed-restricted videos or dismissal immediately return to Windows Mode D + Live PCM |
| **Any Other Windows App / Game / Browser Tab** | **Mode C** (`LivePcm`) + **Mode D** | Studio controls (when GSMTC exposed) + 48 kHz stereo PCM audio | Normal Windows execution; WASAPI loopback streaming | **No** | Covers games, VLC, Plex Web, Twitch Web, Discord calls, Zoom, etc. |

## Capability Probe Hygiene

[`MediaCapabilityProber`](../src/Windows/Media/Handoff.cs) validates candidate URLs before forwarding handoff requests:
- Rejects non-HTTPS schemes (`file://`, `http://`, `javascript:`, `data:`) in production direct handoff.
- Rejects localhost/link-local/RFC1918 host literals (`127.0.0.1`, `localhost`, `::1`, `169.254.*`) unless explicit test overrides are supplied in automated tests.
- Inspects explicit HTML `text/html` MIME responses to prevent `AVPlayer` errors on generic web pages and routes Spotify/YouTube hosts to their compliant provider adapters instead.
