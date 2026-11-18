# Local Integration HTTP API

`LocalApi` listens strictly on loopback (`http://127.0.0.1:23452/v1` by default). All authenticated endpoints require `Authorization: Bearer <64-hex session key>`, cap bodies at `64 KiB`, limit active concurrency (`8` requests, `16` connections, `4` SSE subscribers), and reject any request carrying an `Origin` header (`401`). Only the OAuth CSRF callback `GET /v1/spotify/callback` runs unauthenticated by bearer header and enforces single-use CSPRNG `state` validation (`403` on mismatch).

## Core, Worker, Archive, Action & Event Endpoints

| Method & Path | Purpose | Notes |
|---|---|---|
| `GET /v1/status` | Host, transport, capture, receiver `audioError`, notification status | `503` if disconnected on transport calls; `200` status |
| `POST /v1/jobs` | Submit worker job (`sha256`, `verify`, `metadata`, `text`, `diagnostic`) | Requires valid `jobId`, `kind`, storage basename |
| `GET /v1/jobs/{id}` | Query job status/result | Bounded 64-record history |
| `DELETE /v1/jobs/{id}` | Request cooperative cancellation | Cancels at chunk boundary |
| `POST /v1/archives` | `{path, name}` stream opaque local file to phone storage + SHA-256 commit | Never auto-extracts archives |
| `GET /v1/artifacts/{name}?offset=0&count=32768` | Download verified binary byte range (`<=32 KiB`) | Host validates offset/length |
| `POST /v1/events` | Publish rich notification `{id, source, appName, category, title, body, priority, expiresUtc, sensitive, iconBase64?, actions?}` | Evaluates `EventRouter` rules |
| `GET /v1/events` | List active unexpired host notification history | Up to 64 retained entries |
| `GET /v1/notifications/status` | Inspect `WindowsNotificationCollector` access status | `Allowed`, `Denied`, or `Unavailable` |
| `POST /v1/notifications/request-access` | Prompt interactive Windows notification permission (requires packaged MSIX identity) | Returns updated access state |
| `POST /v1/actions/{id}` | Invoke locally allowlisted action (`media.toggle`, `microphone.toggleMute`, or local configured exe) | Unlisted ID returns `403` |
| `GET /v1/progress` | SSE stream (`event: progress`) for worker jobs | Drop-oldest queue size 8 |

## Hybrid Media & External PiP Endpoints (`0.2.0`)

| Method & Path | Body / Response | Notes |
|---|---|---|
| `GET /v1/media/status` (`or /v1/media/session`) | Current `MediaSessionState` JSON | Shows owner, mode, generation, position, quality cap, capabilities |
| `POST /v1/media/capabilities` | `{url?, filePath?, mode?}` -> `MediaCapabilities` + metadata | Inspects URL/file cleanly before initiating handoff |
| `POST /v1/media/submit` | `{url?, filePath?, mode?, focusPolicy?, qualityCap?, keepScreenAwake?, autoPauseWindowsOnConfirm?, startPositionSeconds?}` | Starts Mode A/B/C/D routing; returns updated `MediaSessionState` |
| `POST /v1/handoff` | `{url}` shorthand -> `MediaRouter` submit result | Kept compatible with 0.1.x callers |
| `POST /v1/media/cancel` | `{reason?}` -> cancels active `TransferPending` handoff | Windows playback untouched |
| `POST /v1/media/return` | `{resumeWindows?}` -> returns ownership from iPhone to Windows | Optional auto-resume of GSMTC |
| `POST /v1/media/command` | `{command, seconds?, qualityCap?, keepScreenAwake?, target?}` | Controls active player (`play`, `pause`, `toggle`, `next`, `previous`, `seek`, `quality`, `keepAwake`) |
| `POST /v1/media/focus` | `{policy}` (`Auto`, `IndependentOnly`, `LivePcmOnly`, `KeepVideoMuteAudio`, `DuckVideo`) | Coordinates `BTAudioSessionCoordinator` + Host Live PCM |
| `POST /v1/media/mode` | `{mode, url?, filePath?}` | Explicit mode override helper |
| `GET /v1/media/events` | SSE `event: media` streaming `MediaSessionState` updates | Used by dashboards/extensions |

## Spotify OAuth PKCE & Connect Endpoints (`0.2.0`)

| Method & Path | Body / Response | Notes |
|---|---|---|
| `GET /v1/spotify/status` | `{connected, playback?, capabilities}` | Auto-refreshes expired tokens via PKCE `refresh_token` |
| `POST /v1/spotify/auth/start` | `{clientId?, redirectUri?}` -> `{authorizeUrl, state, redirectUri}` | Open `authorizeUrl` in local browser |
| `GET /v1/spotify/callback?code=...&state=...` | Exchanges authorization code -> seals DPAPI token locally | Rejects invalid/missing CSRF `state` with `403`/`400` |
| `POST /v1/spotify/disconnect` | Deletes local DPAPI token store | Returns `{connected: false}` |
| `GET /v1/spotify/devices` | `{devices: [...]}` | Lists active Spotify Connect targets |
| `GET /v1/spotify/playlists` | `{playlists: [...]}` | User playlists (up to 20) |
| `POST /v1/spotify/play` / `pause` / `next` / `previous` / `seek` / `transfer` / `queue` | `{uri?, deviceId?, seconds?, play?}` -> `{ok: true}` | Maps `403`/`404`/`429` cleanly (`Retry-After` preserved) |

## PowerShell Example (External PiP Handoff)

```powershell
$headers = @{ Authorization = 'Bearer ' + (Get-Content config/session.key -Raw).Trim().ToLowerInvariant() }
$handoff = @{ url = 'https://cdn.example.com/lecture.mp4'; qualityCap = '720p'; keepScreenAwake = $true; autoPauseWindowsOnConfirm = $true } | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:23452/v1/media/submit -Headers $headers -Method Post -ContentType application/json -Body $handoff
```
