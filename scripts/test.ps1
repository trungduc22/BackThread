param([string]$Dotnet='dotnet',[int]$SoakSeconds=0,[string]$Gcc='gcc')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
 New-Item -ItemType Directory -Force artifacts | Out-Null
 & $Dotnet build tests/BackThread.Tests -c Release --nologo
 if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
 $env:BACKTHREAD_SOAK_SECONDS=[string]$SoakSeconds
 & $Dotnet run --project tests/BackThread.Tests -c Release --no-build
 if ($LASTEXITCODE -ne 0) { throw 'Managed tests failed' }
 if (Get-Command $Gcc -ErrorAction SilentlyContinue) {
  & $Gcc -std=c11 -Wall -Wextra -Werror tests/native/core_test.c src/Shared/Protocol/bt_wire.c src/iOS/AudioReceiver/BTPcmRing.c -o artifacts/native-core-tests.exe
  if ($LASTEXITCODE -ne 0) { throw 'Native tests did not compile' }
  & ./artifacts/native-core-tests.exe
  if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
  & $Gcc -std=c11 -Wall -Wextra -Werror tests/native/ui_test.c -o artifacts/native-ui-tests.exe
  if ($LASTEXITCODE -ne 0) { throw 'Native interaction tests did not compile' }
  & ./artifacts/native-ui-tests.exe
  if ($LASTEXITCODE -ne 0) { throw 'Native interaction tests failed' }
 } else { Write-Warning 'GCC unavailable; native core tests were not run.' }
} finally { Remove-Item Env:BACKTHREAD_SOAK_SECONDS -ErrorAction SilentlyContinue; Pop-Location }
