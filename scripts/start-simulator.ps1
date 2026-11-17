param([string]$Dotnet='dotnet',[int]$Port=23451)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
 if (!(Test-Path config/session.key)) {
  & $Dotnet run --project src/Windows/Host -- --init-key config/session.key
  if ($LASTEXITCODE -ne 0) { throw 'Could not create key' }
 }
 & $Dotnet run --project src/Simulator -- --key-file config/session.key --port $Port
} finally { Pop-Location }
