# Local integration API

Host exposes `http://127.0.0.1:23452/v1` by default. Use a local process, with `Authorization: Bearer <lowercase key hex>`. The API accepts no browser Origin header and does not enable CORS. The bearer credential grants the caller the configured Host's local capabilities; store it privately.

GhostCode does not need to be installed. No GhostCode repository or database is read or modified. These are integration points for a future client.

| Intent | Method and path | Body / response |
|---|---|---|
| QueryStatus | GET `/v1/status` | Connection state, capture diagnostics, receiver audioError, simulated flag |
| SubmitJob | POST `/v1/jobs` | `{jobId,kind,name,expectedSha256?}` → remote state |
| Query job | GET `/v1/jobs/{id}` | Remote state/result/error |
| CancelJob | DELETE `/v1/jobs/{id}` | Cooperative cancellation request |
| SubmitArchive | POST `/v1/archives` | `{path:<absolute Windows file>,name:<safe remote basename>}`; streams and verifies opaque file |
| RetrieveArtifact | GET `/v1/artifacts/{name}?offset=0&count=32768` | Binary range, max 32 KiB; advance offset until empty |
| PublishEvent | POST `/v1/events` | Companion event → accepted/rejected |
| Event history | GET `/v1/events` | Bounded, unexpired host history |
| SubscribeToProgress | GET `/v1/progress` | SSE `event: progress`; JSON state in data; max 4 subscribers, drop-oldest queue size 8 |
| Quick Action | POST `/v1/actions/{id}` | Only locally configured allowlist IDs |
| Direct handoff | POST `/v1/handoff` | `{url}`; requires deviceHasInternet and supported HTTPS resource |

Requests are bounded to 64 KiB. There can be at most eight active API handlers, including SSE subscribers, and sixteen TCP connections. There is no remote listening address setting. A disconnected agent normally returns 503. Invalid data returns 400, unlisted actions 403, missing/incorrect credentials or Origin requests 401, and saturation 429.

```powershell
# Run from the repository root with a trusted local shell.
$headers = @{ Authorization = 'Bearer ' + (Get-Content config/session.key -Raw).Trim().ToLowerInvariant() }
Invoke-RestMethod http://127.0.0.1:23452/v1/status -Headers $headers

$archive = @{ path = 'C:\Backups\project.zip'; name = 'project-2026-11-18.zip' } | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:23452/v1/archives -Headers $headers -Method Post -ContentType application/json -Body $archive

$job = @{ jobId = [guid]::NewGuid().ToString('N'); kind = 'sha256'; name = 'project-2026-11-18.zip' }
Invoke-RestMethod http://127.0.0.1:23452/v1/jobs -Headers $headers -Method Post -ContentType application/json -Body ($job | ConvertTo-Json)
Invoke-RestMethod ("http://127.0.0.1:23452/v1/jobs/" + $job.jobId) -Headers $headers

$event = @{ id = [guid]::NewGuid().ToString('N'); source = 'GhostCode'; title = 'Build complete'; body = 'Ready for review'; priority = 1; expiresUtc = [DateTimeOffset]::UtcNow.AddMinutes(1).ToString('o'); sensitive = $false }
Invoke-RestMethod http://127.0.0.1:23452/v1/events -Headers $headers -Method Post -ContentType application/json -Body ($event | ConvertTo-Json)
```

SubmitArchive stores a file already prepared by the caller. It does not generate a ZIP, extract archives, walk arbitrary directories, or promise transactional multi-file backups. Reusing a destination basename replaces that stored file only after successful checksum validation. Use versioned names to retain old archives.

Supported job examples: `sha256`, `verify` with expectedSha256, `metadata`, `text`, or diagnostic with name `memory` or `version`. No shell strings or arbitrary executable arguments are accepted by job requests.

Quick Action configuration example (add only commands you explicitly authorize):

```json
{"id":"editor.project","title":"Open project","kind":"launch","executable":"C:\\Program Files\\Editor\\Editor.exe","arguments":["C:\\Projects\\example"]}
```

`microphone.toggleMute` uses the default Windows communications input. Switching the Windows default output is not implemented because it would require a different supported mechanism or a deliberately adopted platform-specific dependency.
