param([string]$Dotnet='dotnet',[switch]$Publish,[string]$Output='artifacts/release/Windows')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
 & $Dotnet build BackThread.sln -c Release --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
 if ($Publish) {
  $publishPath=[IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
  $artifactPrefix=[IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if (!$publishPath.StartsWith($artifactPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output must stay under repository artifacts' }
  foreach ($project in @('src/Windows/Host/BackThread.Host.csproj','src/Windows/Tray/BackThread.Tray.csproj','src/Simulator/BackThread.Simulator.csproj','tests/BackThread.DeviceTests/BackThread.DeviceTests.csproj')) {
   & $Dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishPath --nologo
   if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
  }
  New-Item -ItemType Directory -Force (Join-Path $publishPath 'config') | Out-Null
  Copy-Item -LiteralPath config/example.json -Destination (Join-Path $publishPath 'config/example.json') -Force
  Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md -Destination $publishPath -Force
  Copy-Item -LiteralPath scripts/Setup-Portable.ps1 -Destination $publishPath -Force
  Copy-Item -LiteralPath docs/PORTABLE_README.txt -Destination (Join-Path $publishPath 'READ-ME-FIRST.txt') -Force
  & ./scripts/collect-licenses.ps1 -Destination (Join-Path $publishPath 'licenses')
  Copy-Item -LiteralPath src/Windows/Host.Core/Foundation/LICENSE.NativeShellKit.txt -Destination (Join-Path $publishPath 'licenses') -Force
 }
} finally { Pop-Location }
