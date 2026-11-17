param([string]$Destination='artifacts/release/Windows/licenses')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
 $assets=Get-Content src/Windows/Tray/obj/project.assets.json -Raw | ConvertFrom-Json
 $packageRoot=@($assets.packageFolders.PSObject.Properties.Name)[0]
 New-Item -ItemType Directory -Force $Destination | Out-Null
 $paths=@($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { Join-Path $packageRoot $_.Value.path })
 foreach ($name in @('microsoft.netcore.app.runtime.win-x64','microsoft.aspnetcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
  $parent=Join-Path $packageRoot $name
  if (Test-Path -LiteralPath $parent) { $paths+=@(Get-ChildItem -LiteralPath $parent -Directory | ForEach-Object FullName) }
 }
 foreach ($path in ($paths | Sort-Object -Unique)) {
  if (!(Test-Path -LiteralPath $path)) { continue }
  $name=(Split-Path (Split-Path $path -Parent) -Leaf)+'-'+(Split-Path $path -Leaf)
  $notices=@(Get-ChildItem -LiteralPath $path -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|THIRD.*NOTICE)' })
  if ($notices.Count -eq 0) { continue }
  $target=Join-Path $Destination $name; New-Item -ItemType Directory -Force $target | Out-Null
  $notices | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }
 }
 if (Test-Path -LiteralPath docs/licenses) {
  $sdkTarget=Join-Path $Destination 'Windows-SDK-and-CsWinRT'; New-Item -ItemType Directory -Force $sdkTarget | Out-Null
  Get-ChildItem -LiteralPath docs/licenses -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $sdkTarget -Force }
 }
} finally { Pop-Location }
