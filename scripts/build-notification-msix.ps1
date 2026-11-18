param(
 [Parameter(Mandatory=$true)][string]$PublishDir,
 [Parameter(Mandatory=$true)][string]$SdkBin,
 [string]$OutputDir='artifacts/notification-msix'
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$outputPath=[IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$artifactPrefix=[IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(!$outputPath.StartsWith($artifactPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay under repository artifacts'}
$publishPath=(Resolve-Path -LiteralPath $PublishDir).Path
$sdkPath=(Resolve-Path -LiteralPath $SdkBin).Path
$makeAppx=Join-Path $sdkPath 'makeappx.exe';$signTool=Join-Path $sdkPath 'signtool.exe'
foreach($tool in @($makeAppx,$signTool)){if(!(Test-Path -LiteralPath $tool)){throw "Missing SDK tool $tool"};if((Get-AuthenticodeSignature -LiteralPath $tool).Status -ne 'Valid'){throw 'SDK tool signature is not valid'}}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$stage=Join-Path $outputPath ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Get-ChildItem -LiteralPath $publishPath | Copy-Item -Destination $stage -Recurse
Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {if($_.Extension -in @('.key','.pfx','.pem','.secret') -or $_.Name -in @('local.json','apps.json')){throw 'Private runtime data must not enter the package'}}
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging/msix/Package.appxmanifest') -Destination (Join-Path $stage 'AppxManifest.xml')
Add-Type -AssemblyName System.Drawing
$assets=Join-Path $stage 'Assets';New-Item -ItemType Directory -Path $assets | Out-Null
foreach($item in @(@('StoreLogo.png',50),@('Square150x150Logo.png',150),@('Square44x44Logo.png',44))){
 $size=[int]$item[1];$bitmap=New-Object Drawing.Bitmap $size,$size;$graphics=[Drawing.Graphics]::FromImage($bitmap)
 $font=New-Object Drawing.Font 'Segoe UI',($size*.34),([Drawing.FontStyle]::Bold)
 $brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(115,224,199))
 try{$graphics.Clear([Drawing.Color]::FromArgb(9,12,16));$graphics.DrawString('BT',$font,$brush,($size*.12),($size*.24));$bitmap.Save((Join-Path $assets $item[0]),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$font.Dispose();$brush.Dispose();$bitmap.Dispose()}
}
$package=Join-Path $outputPath 'BackThread-Notifications.msix'
& $makeAppx pack /o /d $stage /p $package
if($LASTEXITCODE -ne 0){throw 'MSIX validation/packing failed'}
# A development signing key is generated in memory, never installed or trusted.
$rsa=[Security.Cryptography.RSA]::Create(2048)
$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=BackThreadLocalDev',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$oids=[Security.Cryptography.OidCollection]::new();[void]$oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids,$true))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
$certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(30))
$privateDir=Join-Path $repoRoot 'artifacts/ios-private';New-Item -ItemType Directory -Force -Path $privateDir | Out-Null
$pfx=Join-Path $privateDir ('msix-sign-'+[Guid]::NewGuid().ToString('N')+'.pfx')
try{
 [IO.File]::WriteAllBytes($pfx,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,''))
 & $signTool sign /fd SHA256 /f $pfx $package
 if($LASTEXITCODE -ne 0){throw 'Signing failed'}
 [IO.File]::WriteAllBytes((Join-Path $outputPath 'BackThreadLocalDev.cer'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
 @{subject=$certificate.Subject;thumbprint=$certificate.Thumbprint;notAfter=$certificate.NotAfter.ToUniversalTime().ToString('o');trustedOrInstalled=$false;package=(Split-Path -Leaf $package)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'SIGNING-INFO.json')
}finally{if(Test-Path -LiteralPath $pfx){Remove-Item -LiteralPath $pfx};$certificate.Dispose();$rsa.Dispose()}
Write-Output 'Signed development package prepared. No certificate trust, app installation or Windows permission was changed.'
