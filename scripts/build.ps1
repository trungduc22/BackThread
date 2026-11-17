param([string]$Dotnet='dotnet',[switch]$Publish)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
 & $Dotnet build BackThread.sln -c Release --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
 if ($Publish) {
  foreach ($project in @('src/Windows/Host/BackThread.Host.csproj','src/Windows/Tray/BackThread.Tray.csproj','src/Simulator/BackThread.Simulator.csproj')) {
   & $Dotnet publish $project -c Release -r win-x64 --self-contained true -o 'artifacts/release/Windows' --nologo
   if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
  }
  New-Item -ItemType Directory -Force artifacts/release/Windows/config | Out-Null
  Copy-Item -LiteralPath config/example.json -Destination artifacts/release/Windows/config/example.json -Force
  Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md -Destination artifacts/release/Windows -Force
  Copy-Item -LiteralPath scripts/Setup-Portable.ps1 -Destination artifacts/release/Windows -Force
  Copy-Item -LiteralPath docs/PORTABLE_README.txt -Destination artifacts/release/Windows/READ-ME-FIRST.txt -Force
  & ./scripts/collect-licenses.ps1
 }
} finally { Pop-Location }
