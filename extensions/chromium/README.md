# BackThread Chromium Extension (MV3)

Sends the active browser tab's URL and title to the local BackThread Host through **Chromium Native Messaging** (`BackThread.Host.exe --native-messaging`).

## Security Design

- Uses `activeTab` + `nativeMessaging` permissions **only** (never reads browsing history, cookies, passwords, or arbitrary page DOM).
- Never bypasses BackThread Host's `Origin`-header rejection (`LocalApi.cs`) or enables CORS.
- Never embeds or leaks BackThread's Bearer key in browser extension storage; `BackThread.Host.exe --native-messaging` reads `config/session.key` locally as a native Windows user-session process.
- Respects provider capabilities cleanly: direct HTTPS video/audio plays via `AVPlayer`, Spotify opens Mode D Spotify Connect control, YouTube reports honest capability limits and supports Windows Mode D + USB Live PCM without bypassing DRM or extracting streams.

## Setup (Chrome / Edge / Brave on Windows)

1. Load unpacked extension from `extensions/chromium` in `chrome://extensions` (Developer Mode) and note the Extension ID.
2. Run `pwsh ./extensions/chromium/install-native-host.ps1 -ExtensionId <id> -HostExe <path-to-BackThread.Host.exe>`.
3. Open the extension popup while viewing a tab and click **Send to iPhone**.
