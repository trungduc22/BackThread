# Spotify Integration (Official Web API PKCE + Mode D)

BackThread integrates Spotify strictly through **official Spotify Web APIs with OAuth 2.0 Proof Key for Code Exchange (PKCE)** implemented in [`SpotifyProvider`](../src/Windows/Media/SpotifyProvider.cs) and exposed through [`LocalApi`](../src/Windows/Host/LocalApi.cs) and [`TrayContext`](../src/Windows/Tray/Program.cs).

## Security & DRM Compliance

- **No client secret**: Public PKCE desktop flow uses only the user's configured `spotifyClientId` (`config/local.json`) + dynamically generated SHA-256 `code_challenge` / `code_verifier` and CSPRNG CSRF `state`.
- **Encrypted local token storage**: [`LocalSpotifyTokenStore`](../src/Windows/Media/SpotifyProvider.cs) protects serialized `SpotifyTokenSet` JSON on Windows using `CryptProtectData` (`DPAPI`, `CurrentUser` scope, `"BackThread.Spotify"` entropy label). No tokens are checked into Git, staged in releases, or written to console logs.
- **Zero stream extraction / DRM bypass**: Spotify tracks remain DRM-protected on Spotify's licensed desktop/Connect endpoints. BackThread never extracts audio files onto the iPhone; instead, **Mode D** sends authorized Spotify Connect commands while **Mode C (Live USB PCM)** delivers real-time Windows playback audio over Lightning USB when listening on the iPhone speaker.

## Supported OAuth & Playback Scopes

`BeginAuthorization` requests standard user scopes:
- `user-read-playback-state`
- `user-modify-playback-state`
- `user-read-currently-playing`
- `playlist-read-private`

## Setup & Usage (Windows 10)

1. Register an app in the Spotify Developer Dashboard and add Redirect URI:
   `http://127.0.0.1:23452/v1/spotify/callback`
2. Copy the Client ID (no Client Secret needed) into `config/local.json`:
   ```json
   {
     "spotifyClientId": "YOUR_SPOTIFY_CLIENT_ID",
     "spotifyRedirectUri": "http://127.0.0.1:23452/v1/spotify/callback"
   }
   ```
3. Launch `BackThread.Tray.exe` -> right-click tray icon -> **Spotify -> Connect via OAuth PKCE...** (or trigger `POST /v1/spotify/auth/start` on `127.0.0.1:23452` and open `authorizeUrl`).
4. Approve authorization in the browser; loopback `/v1/spotify/callback` validates `state` against the pending PKCE session (`403` on mismatch), exchanges `code`, and seals the token via Windows DPAPI.

## API Endpoints & Rate-Limit Mapping

All control routes require `Authorization: Bearer <64-hex session key>` and reject browser `Origin` headers:

- `GET /v1/spotify/status`: Connection flag, current track/device/position/duration, and capability summary.
- `POST /v1/spotify/auth/start` & `POST /v1/spotify/disconnect`: Initiate PKCE login or delete local token.
- `GET /v1/spotify/devices` & `GET /v1/spotify/playlists`: Enumerate Spotify Connect target devices & user playlists.
- `POST /v1/spotify/play`, `/pause`, `/next`, `/previous`, `/seek`, `/transfer`, `/queue`: Remote playback control.
- Automatic HTTP `401` token refresh using stored `refresh_token`, plus clean mapping of `403` (`Spotify Premium required / restricted action`), `404` (`No active Spotify Connect device`), and `429` (`Retry-After` preserved) via `SpotifyApiException`.

## Verification Scope

- **Verified in Unit & LocalApi Integration Tests**: PKCE `code_challenge` generation, CSRF state mismatch rejection (`403`), automatic `401` refresh retry, `429`/`404` mapping, playback/device/playlist parsing, and `/v1/spotify/*` loopback routes tested with mock HTTP handlers in [`HybridMediaTests`](../tests/BackThread.Tests/HybridMediaTests.cs).
- **Pending Real-Account Test**: Interactive browser sign-in against live `accounts.spotify.com` and live `api.spotify.com` Spotify Connect device switching were not executed inside the offline sandbox.
