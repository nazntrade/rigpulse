$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root 'tools/PawnIO_setup.exe'
$expected = 'a3a46226c5e2824f4cdd42be0eecbabfc672c86f7889710f5ab1e6ad385b47a0'
New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
if (!(Test-Path -LiteralPath $path)) {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe' -OutFile $path
}
if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'The pinned PawnIO installer hash does not match.' }
$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu') { throw 'The official PawnIO installer signature is not valid.' }
Write-Host 'Verified official PawnIO 2.1.0 installer. The sensor driver is optional and is never installed by the build.'
