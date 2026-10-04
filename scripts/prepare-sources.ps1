$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root 'artifacts/third-party-sources'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sources = @(
    @{name='LibreHardwareMonitor-0.9.6';repo='LibreHardwareMonitor/LibreHardwareMonitor';ref='v0.9.6'},
    @{name='PawnIO-2.1.0';repo='namazso/PawnIO';ref='2.1.0'},
    @{name='PawnPP';repo='namazso/PawnPP';ref='e64e4c37b2d8ba0d8ee57205faf8183aee12c438'},
    @{name='PawnIO-Modules-0.1.6';repo='namazso/PawnIO.Modules';ref='0.1.6'}
)
foreach ($source in $sources) {
    $target = Join-Path $destination ($source.name + '.zip')
    if (!(Test-Path -LiteralPath $target)) { Invoke-WebRequest -Uri "https://codeload.github.com/$($source.repo)/zip/$($source.ref)" -OutFile $target }
}
$sources | ConvertTo-Json | Set-Content (Join-Path $destination 'SOURCE-MANIFEST.json') -Encoding utf8
Copy-Item (Join-Path $root 'licenses') $destination -Recurse -Force
Compress-Archive -Path "$destination/*" -DestinationPath (Join-Path $root 'artifacts/RigPulse-ThirdPartySources.zip') -Force
