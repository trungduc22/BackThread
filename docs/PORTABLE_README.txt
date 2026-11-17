BackThread 0.1 engineering checkpoint — Windows x64

This directory contains self-contained Windows Host, native tray and simulator.
Keep all DLLs, runtime configuration and license files together. A .NET SDK or
system .NET 8 runtime is not required to run these executables.

1. Open PowerShell in this extracted directory.
2. Run: powershell -File Setup-Portable.ps1 -Transport usb
   For local simulation, select -Transport localhost instead.
3. USB mode needs an installed, running BackThread iOS app with the same private
   key provisioned through its Pair screen. This session did not build an iOS app.
4. Run BackThread.Tray.exe, or use:
   .\BackThread.Host.exe --config config/local.json
   Do not run Tray and Host together on the same API port.
5. For simulator mode, in a separate terminal run:
   .\BackThread.Simulator.exe --key-file config/session.key

Device diagnostics: .\BackThread.Host.exe --diagnose
Windows capture check (quiet tone): .\BackThread.Host.exe --audio-probe
Windows media check (silent fixture): .\BackThread.Host.exe --media-self-test

API: http://127.0.0.1:23452/v1, bearer authentication using the lowercase hex key.
No key is included in this package. Keep your generated config/session.key private.

The attached iPhone 6 / iOS 12.5.8 was detected, but SSH and the application port
were refused. No iPhone speaker output, screen-off playback, UI, memory footprint
or installation was verified. No phone firmware/jailbreak/activation changes were
made. See the source package's README and docs for actual feature status and tests.

This is not an end-to-end hardware-verified production release.
