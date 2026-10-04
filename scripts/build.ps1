param([switch]$Signed, [string]$CertificateThumbprint = $env:RIGPULSE_CERT_THUMBPRINT, [string]$SignTool = $env:RIGPULSE_SIGNTOOL, [string]$Compiler = $env:RIGPULSE_ISCC)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'prepare-driver.ps1')
    & (Join-Path $PSScriptRoot 'prepare-sources.ps1')
    $version = ([xml](Get-Content 'src/RigPulse/RigPulse.csproj' -Raw)).Project.PropertyGroup.Version
    if ($Signed -and (!$CertificateThumbprint -or !$SignTool)) { throw 'A code-signing certificate thumbprint and SignTool path are required. No certificate or private key belongs in Git.' }
    dotnet run --project tests/RigPulse.Tests.csproj -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }
    dotnet publish src/RigPulse/RigPulse.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish -p:RestoreLockedMode=true
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    if ($Signed) {
        & $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 artifacts/publish/RigPulse.exe
        if ($LASTEXITCODE) { throw 'Application signing failed.' }
        if ((Get-AuthenticodeSignature artifacts/publish/RigPulse.exe).Status -ne 'Valid') { throw 'Application signature is not trusted.' }
    }
    if (!$Compiler) {
        foreach ($candidate in @('C:\Program Files (x86)\Inno Setup 7\ISCC.exe','C:\Program Files (x86)\Inno Setup 6\ISCC.exe', (Join-Path $root 'tools/Inno/ISCC.exe'))) { if (Test-Path -LiteralPath $candidate) { $Compiler = $candidate; break } }
    }
    if (!$Compiler) { throw 'Install Inno Setup or set RIGPULSE_ISCC to ISCC.exe.' }
    $arguments = @("/DAppVersion=$version")
    if ($Signed) { $arguments += '/DSignedBuild'; $arguments += "/Srigpulse=`"$SignTool`" sign /sha1 $CertificateThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `$f" }
    & $Compiler @arguments installer/RigPulse.iss
    if ($LASTEXITCODE) { throw 'Installer build failed.' }
    $setup = "artifacts/RigPulse-Setup-$version-win-x64.exe"
    if ($Signed -and (Get-AuthenticodeSignature $setup).Status -ne 'Valid') { throw 'Installer signature is not trusted.' }
    Copy-Item artifacts/publish/RigPulse.exe "artifacts/RigPulse-$version-win-x64.exe" -Force
    $files = @($setup, "artifacts/RigPulse-$version-win-x64.exe", 'artifacts/RigPulse-ThirdPartySources.zip')
    $files | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $(Split-Path $_ -Leaf)" } | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
    @{version=$version;authenticodeSigned=[bool]$Signed;builtAtUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content artifacts/build-info.json -Encoding utf8
    Write-Host "Built RigPulse $version. Authenticode signed: $([bool]$Signed)."
} finally { Pop-Location }
