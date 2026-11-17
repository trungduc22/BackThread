param([ValidateSet('usb','localhost')][string]$Transport='usb')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 if (!(Test-Path -LiteralPath './BackThread.Host.exe')) { throw 'Run this script from the extracted portable Windows package.' }
 New-Item -ItemType Directory -Force config | Out-Null
 if (!(Test-Path -LiteralPath config/session.key)) { & ./BackThread.Host.exe --init-key config/session.key; if ($LASTEXITCODE -ne 0) { throw 'Key creation failed' } }
 if (!(Test-Path -LiteralPath config/local.json)) {
  $settings=Get-Content config/example.json -Raw | ConvertFrom-Json; $settings.transport=$Transport
  $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath config/local.json -Encoding utf8
 }
 Write-Host 'Configuration is ready. Keep config/session.key private and provision it to the iOS app through its Pair screen.'
 Write-Host 'USB: run BackThread.Tray.exe or BackThread.Host.exe --config config/local.json.'
 Write-Host 'Simulator: run BackThread.Simulator.exe --key-file config/session.key in a separate terminal, with transport=localhost.'
} finally { Pop-Location }
