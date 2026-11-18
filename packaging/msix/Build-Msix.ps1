# Build-Msix.ps1 — Supported MSIX packaging helper for BackThread.Host / BackThread.Tray
# Requires Windows 10/11 SDK (MakeAppx.exe, SignTool.exe) and an explicit user-provided signing certificate.
# Does NOT silently install root certificates, bypass OS capabilities, or alter Windows Developer Mode.

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$OutputMsix = "BackThread.msix",

    [string]$CertPfxPath = "",

    [SecureString]$CertPassword
)

$ErrorActionPreference = "Stop"

$publishFull = (Resolve-Path $PublishDir).Path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Join-Path $scriptDir "Package.appxmanifest"

if (-not (Test-Path $manifest)) {
    throw "Package.appxmanifest not found at $manifest"
}

$makeAppx = Get-Command "MakeAppx.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $makeAppx) {
    $sdkBin = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\MakeAppx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($sdkBin) { $makeAppx = $sdkBin.FullName }
}
if (-not $makeAppx) {
    throw "MakeAppx.exe not found. Install the Windows 10/11 SDK."
}

$stage = Join-Path $env:TEMP ("backthread-msix-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -Path (Join-Path $publishFull "*") -Destination $stage -Recurse -Force
    Copy-Item -Path $manifest -Destination (Join-Path $stage "AppxManifest.xml") -Force

    # Generate minimal placeholder PNG assets only if user did not supply Assets/ yet.
    $assetsDir = Join-Path $stage "Assets"
    if (-not (Test-Path $assetsDir)) {
        New-Item -ItemType Directory -Path $assetsDir | Out-Null
    }
    foreach ($pngName in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
        $target = Join-Path $assetsDir $pngName
        if (-not (Test-Path $target)) {
            $repoAsset = Join-Path $scriptDir "Assets\$pngName"
            if (Test-Path $repoAsset) {
                Copy-Item $repoAsset $target -Force
            }
            else {
                throw "Missing required MSIX asset: $repoAsset. Supply PNG assets before running MakeAppx."
            }
        }
    }

    & $makeAppx pack /o /d $stage /p $OutputMsix
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE" }

    if ($CertPfxPath -ne "") {
        $signTool = Get-Command "SignTool.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
        if (-not $signTool) {
            $sdkSign = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\SignTool.exe" -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
            if ($sdkSign) { $signTool = $sdkSign.FullName }
        }
        if (-not $signTool) { throw "SignTool.exe not found." }
        $args = @("sign", "/fd", "SHA256", "/f", $CertPfxPath)
        if ($CertPassword) {
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertPassword))
            $args += @("/p", $plain)
        }
        $args += $OutputMsix
        & $signTool @args
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE" }
    }

    Write-Host "MSIX staged at $OutputMsix. To enable real Windows toast mirroring, trust your signing cert in Windows settings and run Add-AppxPackage."
}
finally {
    if (Test-Path $stage) { Remove-Item -Path $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
