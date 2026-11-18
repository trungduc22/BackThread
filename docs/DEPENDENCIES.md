# Dependencies and Repository Research

BackThread remains an original MIT-licensed implementation ([LICENSE](../LICENSE)) built with strict dependency, license, and platform hygiene. Zero GPL `youTFast` code, zero proprietary extraction code, and zero `TetherSender` files are included.

## Runtime & SDK Dependencies

| Dependency | Version / Source | License | Role in BackThread |
|---|---|---|---|
| **.NET 8 SDK / Runtime** | `8.0.428` (`win-x64` self-contained runtime `8.0.34`) | MIT | Core Windows Host, Media, Events, Worker, Transport, Simulator, Tray, Test suite |
| **NAudio.Wasapi / NAudio.Core** | `2.2.1` | MIT | Shared-mode WASAPI loopback capture at 48 kHz stereo PCM16 |
| **Microsoft.Windows.SDK.NET.Ref / CsWinRT** | `10.0.19041.56` / `2.2.0` | Windows SDK License / MIT (`docs/licenses/`) | GSMTC `GlobalSystemMediaTransportControlsSessionManager` & `UserNotificationListener` projections |
| **Windows Crypt32 (`DPAPI`) / Winmm (`timeBeginPeriod`)** | Windows OS APIs (guarded `IsWindows()`) | OS Component | DPAPI `CryptProtectData` Spotify token storage & multimedia timer resolution during audio |
| **iPhoneOS 12.4 SDK + Apple Frameworks** | `UIKit`, `AVFoundation`, `AudioToolbox`, `MediaPlayer`, `WebKit`, `CoreMedia`, `Security`, `ImageIO`, `QuartzCore` | Apple SDK Terms (stubs not redistributed) | ARM64 min iOS `12.0` UIKit app, persistent `AVPlayerLayer`, `AudioQueue`, `WKWebView` opt-in nocookie sheet |

## Cross-Build & Dev Toolchains (Not Bundled in Source/Releases)

- **Zig `0.13.0` (`clang 18.1.6`)**, **LLD `23.1.x`**, **Apple `rcodesign` `0.29.0`**, **Theos-hosted iPhoneOS `12.4` SDK**, **Python 3** (stdlib), and headless Chromium (`render-ui-previews.py` documentation tool only).

## Research & Compliance Notes

- **Spotify Web API OAuth 2.0 PKCE**: Implemented purely from official public OAuth PKCE (`S256`) and Spotify Connect REST specifications (`SpotifyProvider.cs`). Zero `librespot` or DRM-stripping logic.
- **YouTube oEmbed & `youtube-nocookie.com` Embed**: Uses only public YouTube oEmbed JSON (`https://www.youtube.com/oembed`) and opt-in official iframe embeds (`BTYouTubeEmbedController.m`). Zero `youTFast`/`yt-dlp`/extraction logic.
- **Chromium Extension (MV3) & Native Messaging**: Pure standard Chromium extension manifest v3 (`extensions/chromium/`) using minimal `activeTab` + `nativeMessaging` permissions communicating via 4-byte LE stdio frames (`NativeMessagingHost.cs`).
- **Historical Research References Inspected (`0.1.2`)**: `libusbmuxd`, `libimobiledevice`, `vox`, `WindowsMediaController`, `Theos`, and `TetherSender` were examined strictly for protocol/behavior reference without copying or linking any LGPL/GPL/unlicensed code. See `docs/reports/dependency-audit.json` and `artifacts/release/Windows/licenses/`.
