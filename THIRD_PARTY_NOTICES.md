# Third-party notices

BackThread's original source is MIT licensed. This file does not replace licenses shipped with .NET, Windows SDK packages, Apple SDKs, or external tools.

## NAudio.Wasapi 2.2.1 and NAudio.Core 2.2.1

Source: https://github.com/naudio/NAudio

Copyright (c) Mark Heath 2023 (package nuspec). NAudio source copyright 2008–2023 Mark Heath. Used as unmodified binary NuGet packages. MIT License:

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Microsoft .NET and Windows SDK

Self-contained Windows distributions include Microsoft runtime components and original package notices under `licenses`. Do not remove that directory. The included .NET, ASP.NET Core and Windows Desktop runtime version is 8.0.34.

The Windows projection comes from Microsoft.Windows.SDK.NET.Ref 10.0.19041.56. Its RuntimeList.xml lists Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll as runtime files; the original Windows SDK license is included as `Windows-SDK-license.rtf`. License source: https://aka.ms/WinSDKLicenseURL (Microsoft's sdk_license.rtf). The C#/WinRT runtime is MIT licensed, Copyright (c) Microsoft Corporation; its full notice is included as `CsWinRT-LICENSE.txt`, from https://github.com/microsoft/CsWinRT. Preserve the SDK's redistribution conditions and the associated notices. Build-time targeting references/metadata are not distributed as standalone SDK products.

## Inspected projects not redistributed

libusbmuxd, libimobiledevice, vox, WindowsMediaController, Theos, Theos templates and TetherSender are research references. Their code is not included or linked. No TetherSender license grant is assumed. Optional external OpenSSH/iproxy installations remain governed by their own licenses.
