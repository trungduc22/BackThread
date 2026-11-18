param(
    [Parameter(Mandatory = $true)][string]$ExtensionId,
    [Parameter(Mandatory = $true)][string]$HostExe,
    [ValidateSet('Chrome', 'Edge')][string]$Browser = 'Chrome'
)

$ErrorActionPreference = 'Stop'
$resolvedHost = (Resolve-Path $HostExe).Path
$manifestDir = Join-Path $env:LOCALAPPDATA 'BackThread\NativeMessaging'
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
$manifestPath = Join-Path $manifestDir 'app.backthread.host.json'

$manifest = @{
    name            = 'app.backthread.host'
    description     = 'BackThread Local Host Native Messaging Bridge'
    path            = $resolvedHost
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

$manifest | Set-Content -Path $manifestPath -Encoding UTF8
$regPath = if ($Browser -eq 'Edge') {
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\app.backthread.host'
} else {
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\app.backthread.host'
}
New-Item -Path $regPath -Force | Out-Null
Set-ItemProperty -Path $regPath -Name '(default)' -Value $manifestPath
Write-Host "Registered Native Messaging host at $regPath -> $manifestPath"
