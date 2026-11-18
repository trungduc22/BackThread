# Optional MSIX Packaging for Windows Notification Listener

Windows' `UserNotificationListener` WinRT API requires a packaged application identity (or a Sparse Package registered against the published folder) declaring `<uap3:Capability Name="userNotificationListener" />` plus interactive user consent via `UserNotificationListener.Current.RequestAccessAsync()`.

## Supported Workflows

1. **Unpackaged Portable Mode (default)**:
   - Run `BackThread.Host.exe` or `BackThread.Tray.exe` directly with no installation.
   - Official Windows toast listener reports `Unavailable` without package identity; notifications are published through the authenticated local HTTP API (`POST /v1/events`) or integrations (CLI, browser extension, CI/build scripts).
2. **Optional Packaged / Sparse Registration on Windows 10 (19045)**:
   - Place monochrome/RGBA app icons under `Assets\` next to `Package.appxmanifest` and package using `.\Build-Msix.ps1 -PublishDir <path-to-win-x64-publish> -CertPfxPath <path-to-your-dev-cert.pfx>` (which invokes `MakeAppx.exe` and optional `SignTool.exe` without modifying Windows Developer Mode or certificate stores automatically), OR register as a Sparse Package alongside the published Windows output.
   - Once launched with package identity, open the Tray menu (`Notifications -> Request Windows Notification Permission`) or call `POST /v1/notifications/request-access` on `127.0.0.1:23452` from an interactive desktop session to show the Windows consent prompt.
   - Use `GET /v1/notifications/diagnostics` to inspect `HasPackageIdentity`, `AccessStatus`, poll counts, and guidance.

> Status notice: `Package.appxmanifest` and `Build-Msix.ps1` have been authored and verified by inspection in the repository; MSIX signing and live Windows notification listener runtime acceptance require a Windows 10 machine with Windows SDK tools and are documented honestly as unexecuted on Linux.
