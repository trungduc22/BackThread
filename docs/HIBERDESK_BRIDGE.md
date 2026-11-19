# HiberDesk local bridge v1

Run the ordinary Windows host with its existing configuration. To enable the HiberDesk listener in standalone mode, set `enableHiberDeskBridge` to `true` in that configuration and restart BackThread. HiberDesk can also launch just `BackThread.Host.exe --hiberdesk --config <file>` on request. Run both apps under the same Windows user and elevation level. The installed iOS companion and BTH1 wire protocol are unchanged by this bridge.

Both the standalone TCP listener and the optional same-user named pipe use the existing HTTP API implementation. Port 23452 remains the default. The pipe is `BackThread.HiberDesk.v1.<Windows SID>.<API port>`. It requires a bearer derived with HMAC-SHA256 over the UTF-8 label `BackThread:local-api:hiberdesk:v1` using the existing 32-byte session key. Only the derived credential is sent over the pipe. The derived credential is rejected by the TCP listener, and the USB session key is rejected by the pipe. Authenticated pipe responses advertise `X-BackThread-Bridge: 1`. The pipe allowlist covers status, media, audio, limited Spotify control, event submission/history, and read-only worker status. Archive and job submission, native messaging, arbitrary actions, and notification collection consent stay outside this capability set.

The same per-user host lease applies to tray and CLI hosts. BackThread acquires the lease and starts its local listeners before opening a device session. Separate local fixtures can use separate API ports. Installed binaries from earlier versions do not participate in this lease. The opt-in HiberDesk controller also checks whether a standalone listener is already running before launching.

Submissions from the HiberDesk pipe honor the host's `pauseWindowsOnConfirmedHandoff` preference. The host can veto auto-pause, and the media router remains responsible for confirmation.

`POST /v1/events` over the HiberDesk pipe returns `routerAccepted`, `deviceAcknowledged`, and nullable `deviceAccepted`. The iPhone's reply covers acceptance of the event, with no claim of visible playback or banner display. The existing TCP response and iPhone UI stay compatible. When the optional Windows collector is enabled alongside the bridge, it skips known HiberDesk and GhostCode notifications. Those applications use the explicit relay policy.

Remove `enableHiberDeskBridge` or set it to `false` and launch BackThread without `--hiberdesk` to close the pipe. Keep the configured session key, iPhone pairing, and Spotify data in their original locations.
