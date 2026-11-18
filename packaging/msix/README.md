# Windows notification Host package

The portable Host cannot use UserNotificationListener without package identity. This package declares runFullTrust/userNotificationListener, preserves ID-only app launch and USB authentication, and still requires Windows consent. Tray > Windows notifications opens a foreground setup/test form.

Build a self-contained Tray first; run scripts/build-notification-msix.ps1 with PublishDir and the verified Windows SDK x64 bin directory. The script validates the manifest using MakeAppx, signs a development MSIX with an in-memory generated key, exports only a public CER, then deletes its temporary private PFX. SDK tools/private keys/configuration are not shipped. A future newly generated signing certificate requires its own trust approval; use a stable approved signing identity for long-term distribution.

Installation needs the exact publisher certificate in Local Computer / Trusted People, not Trusted Root. Obtain explicit administrator authorization before this trust change. CurrentUser / TrustedPeople was rejected with0x800B0109 on the tested Windows10 system; its temporary trust was rolled back. Do not disable execution policy or Developer Mode. Where script execution is restricted, the approved native installation can be performed with ordinary interactive Windows PowerShell Add-AppxPackage after trust is configured; do not change policy to run this helper.

Use scripts/activate-notification-host.ps1 with the existing connection JSON to activate the installed package through the Windows application activation API. Direct execution from WindowsApps is denied; do not change WindowsApps ACLs. Run only one Host. The selected config/session key stays outside the MSIX. First-launch file selection can be avoided by passing the existing configuration at activation.

Evidence: docs/reports/build8-windows-toast-delivery.json. A genuine owned Windows toast was collected and its exact win-ID was visible on the physical iPhone. This verifies delivery on the currently installed client; build8 visual changes need separate phone installation. Content remains masked by existing privacy preferences. No permission or trust requirement is bypassed.

References: https://learn.microsoft.com/en-us/windows/uwp/design/shell/tiles-and-notifications/notification-listener and https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide
