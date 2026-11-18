param([Parameter(Mandatory=$true)][string]$PackagePath,[Parameter(Mandatory=$true)][string]$CertificatePath,[switch]$Install)
$ErrorActionPreference='Stop'
$package=(Resolve-Path -LiteralPath $PackagePath).Path
$certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
if($certificate.Subject -ne 'CN=BackThreadLocalDev' -or $certificate.HasPrivateKey){throw 'Unexpected development publisher certificate'}
if(!$Install){Write-Output "Prepared publisher: $($certificate.Subject), thumbprint: $($certificate.Thumbprint). Explicit -Install and user authorization are required to trust and install.";exit}
$store=[Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople','LocalMachine')
try{
 $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
 if($store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,$certificate.Thumbprint,$false).Count -eq 0){throw 'Signing certificate is not trusted by the machine. Explicit administrator authorization for Local Computer / Trusted People is required; this script does not change trust.'}
 Add-AppxPackage -Path $package
 Get-AppxPackage -Name BackThread.CompanionHost | Select-Object Name,Version,PackageFamilyName
}finally{$store.Close();$certificate.Dispose()}
