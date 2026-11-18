param([Parameter(Mandatory=$true)][string]$ConfigPath)
$ErrorActionPreference='Stop'
$config=(Resolve-Path -LiteralPath $ConfigPath).Path
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class BackThreadPackageActivation {
 [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]private class Manager{}
 [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IManager{
  [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string args,uint options,out uint pid);
  [PreserveSig]int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string verb,out uint pid);
  [PreserveSig]int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr items,out uint pid);
 }
 public static uint Launch(string config){var manager=(IManager)new Manager();uint pid;int hr=manager.ActivateApplication("BackThread.CompanionHost_pdfhpqf2xz9x0!BackThreadTray","\""+config+"\"",0,out pid);Marshal.ThrowExceptionForHR(hr);return pid;}
}
'@
[BackThreadPackageActivation]::Launch($config)
