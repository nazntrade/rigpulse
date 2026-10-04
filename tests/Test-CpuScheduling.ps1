param(
    [string]$ApplicationPath = (Join-Path $PSScriptRoot '..\artifacts\publish\RigPulse.exe'),
    [string]$BaselinePath,
    [switch]$Layout,
    [switch]$LoadWorker,
    [long]$AffinityMask,
    [string]$SignalDirectory
)
$ErrorActionPreference = 'Stop'
if ($LoadWorker) {
    $process = [Diagnostics.Process]::GetCurrentProcess()
    $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::Normal
    $process.ProcessorAffinity = [IntPtr]$AffinityMask
    Add-Type -TypeDefinition @"
using System;
using System.Diagnostics;
public static class SchedulingLoad {
    public static long Burn(int milliseconds) {
        var watch = Stopwatch.StartNew(); long value = 1;
        while (watch.ElapsedMilliseconds < milliseconds) {
            for (int i = 0; i < 10000; i++) value = unchecked(value * 6364136223846793005L + 1);
        }
        return value;
    }
}
"@
    [void][SchedulingLoad]::Burn(1)
    [IO.File]::WriteAllText((Join-Path $SignalDirectory 'ready'), 'ready')
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath (Join-Path $SignalDirectory 'start'))) {
        if ($wait.Elapsed.TotalSeconds -gt 20) { throw 'Load start signal timed out.' }
        Start-Sleep -Milliseconds 50
    }
    [void][SchedulingLoad]::Burn(5000)
    exit 0
}
# Saturate one logical CPU shared by the monitor and all its children. This
# reproduces scheduler starvation without heating the whole CPU with an AVX test.
$current = [Diagnostics.Process]::GetCurrentProcess()
$originalPriority = $current.PriorityClass
$originalAffinity = $current.ProcessorAffinity
$core = [Math]::Min([Environment]::ProcessorCount - 1, 62)
$mask = [long]1 -shl $core
$reports = @()
try {
    $current.PriorityClass = [Diagnostics.ProcessPriorityClass]::AboveNormal
    foreach ($case in @(@{Name='baseline'; Path=$BaselinePath}, @{Name='fixed'; Path=$ApplicationPath})) {
        if (-not $case.Path) { continue }
        $application = (Resolve-Path -LiteralPath $case.Path).Path
        $directory = Join-Path ([IO.Path]::GetTempPath()) ('rigpulse-scheduling-' + [guid]::NewGuid())
        [void][IO.Directory]::CreateDirectory($directory)
        $output = Join-Path $directory ($(if ($Layout) { 'layout.png' } else { 'readings.json' }))
        $load = $null; $monitor = $null
        try {
            $launch = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
            $launch.UseShellExecute = $false; $launch.CreateNoWindow = $true
            foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-LoadWorker', '-AffinityMask', "$mask", '-SignalDirectory', $directory)) { $launch.ArgumentList.Add($argument) }
            $load = [Diagnostics.Process]::Start($launch)
            $wait = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path -LiteralPath (Join-Path $directory 'ready'))) {
                if ($load.HasExited -or $wait.Elapsed.TotalSeconds -gt 20) { throw 'CPU load helper did not become ready.' }
                Start-Sleep -Milliseconds 50
            }
            # Mirror the real scheduled task; Windows inherits BelowNormal and
            # the CPU affinity into the monitor and its worker processes.
            $current.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal
            $current.ProcessorAffinity = [IntPtr]$mask
            try {
                $start = [Diagnostics.ProcessStartInfo]::new($application)
                $start.UseShellExecute = $false; $start.CreateNoWindow = $true
                foreach ($argument in @($(if ($Layout) { '--layout-test' } else { '--diagnostics' }), '--output', $output)) { $start.ArgumentList.Add($argument) }
                $monitor = [Diagnostics.Process]::Start($start)
            } finally {
                $current.PriorityClass = [Diagnostics.ProcessPriorityClass]::AboveNormal
                $current.ProcessorAffinity = $originalAffinity
            }
            Start-Sleep -Milliseconds 2500
            $load.Refresh(); $loadCpuBefore = $load.TotalProcessorTime.TotalSeconds
            $loadWall = [Diagnostics.Stopwatch]::StartNew()
            [IO.File]::WriteAllText((Join-Path $directory 'start'), 'start')
            if (-not $load.WaitForExit(10000)) { throw 'Bounded CPU load did not stop.' }
            $loadWall.Stop(); $load.Refresh()
            $loadPercent = 100 * ($load.TotalProcessorTime.TotalSeconds - $loadCpuBefore) / $loadWall.Elapsed.TotalSeconds
            if (-not $monitor.WaitForExit(20000)) { throw 'Diagnostics did not exit after the CPU load stopped.' }
            if ($monitor.ExitCode -ne 0) { throw 'Diagnostics exited unsuccessfully.' }
            if ($Layout) {
                $layoutResult = Get-Content -LiteralPath ($output + '.json') -Raw | ConvertFrom-Json
                $uiGaps = for ($i = 1; $i -lt $layoutResult.captureTimesSeconds.Count; $i++) { $layoutResult.captureTimesSeconds[$i] - $layoutResult.captureTimesSeconds[$i-1] }
                $maxUiGap = ($uiGaps | Measure-Object -Maximum).Maximum
                [pscustomobject]@{Case=$case.Name; LoadPercent=[Math]::Round($loadPercent,1); UiSamples=$layoutResult.widths.Count; MaxUiGapSeconds=$maxUiGap; StableWidth=$layoutResult.stable; Width=$layoutResult.widths[0]; ProcessPriority=$layoutResult.processPriority; DiagnosticPath=$output} | ConvertTo-Json
                if ($case.Name -eq 'fixed' -and ($loadPercent -lt 85 -or $layoutResult.widths.Count -ne 6 -or -not $layoutResult.stable -or $maxUiGap -gt 1.5 -or $layoutResult.processPriority -ne 'AboveNormal')) { throw 'The WPF window stopped refreshing or resized during CPU contention.' }
                continue
            }
            $samples = @(Get-Content -LiteralPath $output -Raw | ConvertFrom-Json)
            $frames = @($samples | ForEach-Object { $_.system.Timestamp } | Where-Object { $_ } | Sort-Object -Unique)
            $gaps = for ($i = 1; $i -lt $frames.Count; $i++) { ([DateTimeOffset]$frames[$i] - [DateTimeOffset]$frames[$i-1]).TotalSeconds }
            $maximumGap = if ($gaps) { ($gaps | Measure-Object -Maximum).Maximum } else { $null }
            $live = @($samples | Where-Object { $_.system -and $_.capturedAt -and ([DateTimeOffset]$_.capturedAt - [DateTimeOffset]$_.system.Timestamp).TotalSeconds -lt 2 }).Count
            $report = [pscustomobject]@{Case=$case.Name; SaturatedLogicalCpu=$core; LoadPercent=[Math]::Round($loadPercent,1); Samples=$samples.Count; DistinctSystemFrames=$frames.Count; MaxSystemGapSeconds=$maximumGap; FreshSystemSamples=$live; ProcessPriorities=@($samples.processPriority | Sort-Object -Unique); SystemWorkerPriorities=@($samples.systemWorker.priority | Sort-Object -Unique); SensorWorkerPriorities=@($samples.sensorWorker.priority | Sort-Object -Unique); DiagnosticPath=$output}
            $reports += $report
            $report | ConvertTo-Json -Depth 4
            if ($case.Name -eq 'fixed') {
                if ($loadPercent -lt 85 -or $frames.Count -lt 7 -or $maximumGap -gt 2 -or $live -lt 7) { throw 'Fresh CPU/RAM frames did not continue during saturated CPU contention.' }
                foreach ($priority in @($samples.processPriority + $samples.systemWorker.priority + $samples.sensorWorker.priority)) {
                    if ($priority -ne 'AboveNormal') { throw 'The UI or a worker retained scheduler-inherited priority.' }
                }
            }
        } finally {
            foreach ($owned in @($monitor, $load)) { if ($owned) { try { if (-not $owned.HasExited) { $owned.Kill($true); [void]$owned.WaitForExit(3000) } } finally { $owned.Dispose() } } }
        }
    }
} finally { $current.PriorityClass = $originalPriority; $current.ProcessorAffinity = $originalAffinity }
if ($Layout) { Write-Host 'PASS: WPF refresh and stable width survive CPU contention.' } else { Write-Host 'PASS: live CPU/RAM polling survives scheduler-inherited BelowNormal and CPU contention.' }
