BackThread 0.2.0 — Windows x64 Self-Contained Portable Package

Self-contained Windows Host, native Tray, Simulator, DeviceTests, Chromium
Native Messaging extension, and optional MSIX manifest are included. Keep DLLs,
runtime configuration, and notices together. No .NET SDK/runtime install is
needed to run these executables. No session key, OAuth client secret, or private
token is shipped.

1. Quick setup
   Open PowerShell in this directory and run:
     powershell -File Setup-Portable.ps1 -Transport usb
   (or -Transport localhost for simulator testing without an iPhone).
   Keep config/session.key private and provision the 64-hex key onto the
   BackThread iOS app under Setup -> Pair.

2. Running BackThread on Windows
   Start EITHER BackThread.Tray.exe OR BackThread.Host.exe --config config/local.json.
   Do not run both on the same API port (default 23452).
   Optional integrations included in this folder:
   - extensions/chromium: unpacked MV3 extension + install-native-host.ps1 for
     sending the active browser tab URL to BackThread via Native Messaging.
   - packaging/msix: optional Package.appxmanifest + documentation for packaged
     Windows UserNotificationListener consent (Windows 10 build 19045).
   - Spotify Connect: set your Spotify app Client ID (PKCE, no client secret) in
     config/local.json ("spotifyClientId") and connect from the Tray menu or
     POST /v1/spotify/connect on 127.0.0.1:23452.

3. Physical USB acceptance & simulator
   BackThread.DeviceTests.exe --key-file config/session.key
   BackThread.DeviceTests.exe --key-file config/session.key --fixture --seconds 3 --worker
   BackThread.Simulator.exe --key-file config/session.key
   BackThread.Host.exe --diagnose | --audio-probe | --media-self-test

4. Verification & trust boundaries
   - Loopback HTTP API binds to 127.0.0.1:23452 with Bearer session-key auth and
     strict browser Origin rejection.
   - Direct HTTPS media streams (MP4/HLS/audio) and Mode B buffered USB transfer
     handoff are supported over USB control + Wi-Fi or Mode B chunks; YouTube
     extraction or cipher decoding is intentionally NOT performed (compliant
     oEmbed metadata + Windows Mode D control + Live PCM / opt-in embed only).
   - Verify bundle integrity with checksums.sha256 before running on Windows.
   - Linux cross-builds and simulator soak checks do not substitute for physical
     Windows GUI, speaker listening, and iPhone 6 acceptance tests.
