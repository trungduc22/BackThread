# Third-party notices

BackThread's original source is MIT licensed. This file does not replace licenses shipped with .NET, Windows SDK packages, Apple SDKs, or external tools.

## NAudio.Wasapi 2.2.1 and NAudio.Core 2.2.1

Source: https://github.com/naudio/NAudio

Copyright (c) Mark Heath 2023 (package nuspec). NAudio source copyright 2008–2023 Mark Heath. Used as unmodified binary NuGet packages. MIT License:

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## QRCoder 1.6.0

Source: https://github.com/codebude/QRCoder

Copyright (c) 2013-2024 Raffael Herrmann. Used as an unmodified NuGet package for local QR pairing code generation. MIT License.

## NativeShellKit

Source: https://github.com/trungduc22/NativeShellKit

Copyright (c) trungduc22. Used for `BackThread.Daemon` (non-UI tray, DPAPI, single-instance, HMAC Named Pipe IPC) and `BackThread.App` (on-demand WinUI 3 Fluent shell). MIT License.

## Microsoft .NET and Windows SDK

Self-contained Windows distributions include Microsoft runtime components and original package notices under `licenses`. Do not remove that directory. BackThread targets .NET 10 (`net10.0` / `net10.0-windows10.0.19041.0`) and Microsoft.WindowsAppSDK `2.6.4` (for `BackThread.App` only).

The Windows projection comes from Microsoft.Windows.SDK.NET.Ref. The C#/WinRT runtime is MIT licensed, Copyright (c) Microsoft Corporation; preserve the SDK's redistribution conditions and associated notices.

## Inspected / referenced projects

- **PhoneBox** (https://github.com/ersinkoc/phonebox, MIT License, Copyright (c) Ersin Koc): inspired architectural patterns adapted independently in C#/Objective-C for BackThread's pinned Wi-Fi TLS + single-use QR pairing, per-app audio mixer, GPU/process/dev monitors, automation rules, Charge Guard, and safe feed proxy. No PhoneBox source files are copied directly.
- libusbmuxd, libimobiledevice, vox, WindowsMediaController, Theos, Theos templates and TetherSender are research references. Their code is not included or linked. No TetherSender license grant is assumed. Optional external OpenSSH/iproxy installations remain governed by their own licenses.
