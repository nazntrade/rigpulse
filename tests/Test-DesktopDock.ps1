param([string]$Executable=(Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/publish/RigPulse.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root ('artifacts/dock-validation-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')
if(-not (Test-Path -LiteralPath $Executable)){throw 'Build RigPulse first.'}
$process=Start-Process -FilePath $Executable -WindowStyle Hidden -ArgumentList ('--dock-test --output "'+$output+'"') -PassThru
if(-not $process.WaitForExit(45000)){$process.Kill();throw 'Dock test timed out; its test process was stopped.'}
if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output)){throw 'Close the existing RigPulse overlay before running this test.'}
$test=Get-Content -LiteralPath $output -Raw|ConvertFrom-Json
if($test.snapshots.Count -ne 14){throw 'Dock cycle did not finish.'}
$baseline=$test.snapshots[0]
$docked=@($test.snapshots|Where-Object {$_.docked})
foreach($snapshot in $docked){
    if(-not $snapshot.registered -or $snapshot.left -ne $baseline.workLeft -or
       $snapshot.width -ne ($baseline.workRight-$baseline.workLeft) -or
       ($snapshot.top+$snapshot.height) -ne $baseline.workBottom -or
       $snapshot.workBottom -ne $snapshot.top){throw 'Panel does not exactly occupy its reserved area.'}
    if($snapshot.padding -ne 2 -or $snapshot.cornerRadius -ne 0){throw 'Docked padding or corners are incorrect.'}
}
if($docked.Count -ne 6 -or @($docked.width|Select-Object -Unique).Count -ne 1 -or @($docked.height|Select-Object -Unique).Count -ne 1){throw 'Docked size is unstable.'}
foreach($snapshot in $test.snapshots){
    foreach($metric in $baseline.valueWidths.PSObject.Properties){
        if($snapshot.valueWidths.($metric.Name) -ne $metric.Value){throw 'Value cells stretched between modes.'}
    }
    if(-not $snapshot.docked -and ($snapshot.workBottom -ne $baseline.workBottom -or $snapshot.width -ne $baseline.width)){throw 'Compact mode or work area was not restored.'}
}
if($test.afterClose.registered -or $test.afterClose.workBottom -ne $baseline.workBottom){throw 'Exit did not release the reservation.'}
Write-Host ('PASS: full-width reservation, stable cells, compact restoration and exit cleanup. Report: '+$output)
