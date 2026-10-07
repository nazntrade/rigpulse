using RigPulse;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var now = DateTimeOffset.UtcNow;
var gpu = new GpuFrame("/gpu-nvidia/1", "Second NVIDIA GPU", "GpuNvidia", 1, 35, .4, 15.9);
var sensor = new SensorFrame(now, 32, [gpu, gpu with { Id = "/gpu-nvidia/0" }, gpu with { Id = "/gpu-intel/0", Type = "GpuIntel" }], [new("fan", "CPU Fan", 700)]);
var low = Metrics.Build(new(now, 1, 9.8, 63.7), sensor, false, 3, now);
var high = Metrics.Build(new(now, 100, 63.7, 63.7), sensor with { CpuTemperature = 100 }, false, 3, now);
Check(low.Select(m => (m.Id, m.Characters)).SequenceEqual(high.Select(m => (m.Id, m.Characters))), "Numeric changes must not change cell widths or topology.");
Check(low.Count(m => m.Id.EndsWith("-load") && m.Label.StartsWith("GPU")) == 2, "Both discrete GPUs must be displayed, without Intel substitution.");
var stale = Metrics.Build(new(now, 100, 10, 64), sensor with { Timestamp = now.AddSeconds(-10) }, false, 3, now);
Check(stale.Single(m => m.Id == "cpu-load").Text == "100%", "CPU must remain live when hardware sensors stall.");
Check(stale.Single(m => m.Id == "cpu-temp").Text == "--", "Stale values must not masquerade as current readings.");
Check(stale.Select(m => m.Id).SequenceEqual(low.Select(m => m.Id)), "Stale sensors must retain the panel topology.");
Check(Metrics.Number(double.NaN, "0") == "--", "Invalid sensor values must be unavailable.");
Check(Metrics.Memory(1.2, 15.9) == "1.2/15.9G", "Formatting must not depend on the Windows language.");
Check(!Metrics.Fresh(now.AddMinutes(1), now), "Future timestamps must not remain fresh forever.");
Console.WriteLine("PASS: stable layout, multi-GPU identity, independent freshness, invalid values and invariant formatting.");

if (OperatingSystem.IsWindows())
{
    using var current = System.Diagnostics.Process.GetCurrentProcess();
    var original = current.PriorityClass;
    try
    {
        current.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        Scheduling.ConfigureCurrentProcess();
        current.Refresh();
        Check(current.PriorityClass == System.Diagnostics.ProcessPriorityClass.AboveNormal,
            "Monitoring must override the BelowNormal priority inherited from Task Scheduler.");
    }
    finally { current.PriorityClass = original; }
}
Console.WriteLine("PASS: scheduler-inherited BelowNormal is overridden for responsive polling.");

var references = new Settings();
var peakSensors = sensor with { CpuTemperature = 99, Fans = [new("cpu-fan", "CPU Fan", 1818), new("case-back", "System Fan #1", 1263), new("case-front", "System Fan #2", 1294)] };
var peakMetrics = Metrics.Build(new(now, 100, 62, 64), peakSensors, false, 3, now, references);
foreach (var id in new[] { "cpu-load", "cpu-temp", "ram", "cpu-fan", "case-back", "case-front" })
    Check(Metrics.Tone(peakMetrics.Single(m => m.Id == id)) == MetricTone.Critical, "Screenshot peak must use light red: " + id);
var gpuPeaks = Metrics.Build(new(now, 5, 10, 64), sensor with { Gpus = [gpu with { Load = 100, Temperature = 86, Used = 15.2 }] }, false, 3, now);
foreach (var metric in gpuPeaks.Where(m => m.Id.StartsWith(gpu.Id) && !m.Id.EndsWith("-power")))
    Check(Metrics.Tone(metric) == MetricTone.Critical, "GPU load, temperature and memory need independent critical colors.");
Check(Metrics.Tone(low.Single(m => m.Id == "cpu-temp")) == MetricTone.Low, "Idle temperatures must be green.");
Check(Metrics.Tone(new("cpu-temp", "", 5, "70°C", 70)) == MetricTone.Medium, "Warm CPU must be yellow.");
Check(Metrics.Tone(new("cpu-temp", "", 5, "82°C", 82)) == MetricTone.High, "Hot CPU must be orange.");
Check(Metrics.Tone(new("gpu-temp", "", 5, "78°C", 78)) == MetricTone.High, "GPU temperature must have its own thresholds.");
var invalidFan = Metrics.Build(new(now, 1, 10, 64), sensor with { Fans = [new("bad-fan", "CPU Fan", double.NaN)] }, false, 3, now);
Check(Metrics.Tone(invalidFan.Single(m => m.Id == "bad-fan")) == MetricTone.Unavailable, "Invalid fan readings must be gray, not green.");
Check(Metrics.Tone(stale.Single(m => m.Id == "fan")) == MetricTone.Unavailable, "Stale fan readings must not show a current speed color.");
references.FanMaxRpm["case-back"] = 2500;
var overridden = Metrics.Build(new(now, 1, 10, 64), peakSensors, false, 3, now, references);
Check(Metrics.Tone(overridden.Single(m => m.Id == "case-back")) == MetricTone.Low, "Per-sensor reference must override the system fan default.");
Check(Metrics.Tone(overridden.Single(m => m.Id == "case-front")) == MetricTone.Critical, "One fan's override must not change another fan.");
var restoredSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(references))!;
Check(restoredSettings.FanMaxRpm["case-back"] == 2500 && restoredSettings.CpuFanMaxRpm == 1800, "Fan references must survive saving settings.");
references.CpuFanMaxRpm = 0; references.SystemFanMaxRpm = int.MaxValue; references.FanMaxRpm["bad"] = 0; references.Normalize();
Check(references.CpuFanMaxRpm > 0 && references.SystemFanMaxRpm <= 30000 && !references.FanMaxRpm.ContainsKey("bad"), "Invalid references must not divide by zero or remain enabled.");
Console.WriteLine("PASS: four colors, screenshot fan peaks, distinct temperature thresholds, stale/invalid readings and independent fan references.");

var powerSensors = sensor with { CpuPowerWatts = 35, Gpus = [gpu with { PowerWatts = 120, DriverIndex = 1 }, gpu with { Id = "/gpu-nvidia/0", PowerWatts = 110, DriverIndex = 0 }, gpu with { Id = "/gpu-intel/0", Type = "GpuIntel", PowerWatts = 20 }] };
var powerMetrics = Metrics.Build(null, powerSensors, false, 3, now);
Check(powerMetrics.Last().Id == "power-sum" && powerMetrics.Last().Text == "265W", "Power belongs at the end and excludes integrated GPU double counting.");
Check(Metrics.Build(null, powerSensors, false, 3, now, new Settings { ShowComponentPower = false }).All(m => !m.Id.Contains("power")), "Power toggle must remove the entire block.");
Check(Metrics.Build(null, powerSensors with { CpuPowerWatts = null }, false, 3, now).Last().Text == "--", "Incomplete power must not appear to be a complete sum.");
Check(Metrics.Build(null, powerSensors with { Timestamp = now.AddSeconds(-10) }, false, 3, now).Last().Text == "--", "Stale power must be unavailable.");
Check(Metrics.Build(null, powerSensors with { CpuPowerWatts = double.NaN }, false, 3, now).Last().Text == "--", "Invalid power must be unavailable.");
Check(System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(new Settings { ShowComponentPower = false }))!.ShowComponentPower == false, "Power setting must persist.");
Console.WriteLine("PASS: component power ordering, sum, integrated exclusion, toggle, missing and stale sensors.");
Check(!new Settings().DockAboveTaskbar, "Existing installations must keep the compact overlay by default.");
var oldSettings = System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"FontSize\":14,\"StartAtLogin\":true}")!;
Check(!oldSettings.DockAboveTaskbar && oldSettings.FontSize == 14 && oldSettings.StartAtLogin, "Old settings must retain their layout and sign-in options.");
foreach (bool enabled in new[] { true, false })
{
    var restored = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(new Settings { DockAboveTaskbar = enabled }))!;
    Check(restored.DockAboveTaskbar == enabled, "Docking choice must survive saving settings.");
}
Console.WriteLine("PASS: optional docking defaults, old settings compatibility and persistent toggle.");

var visibility = new Settings { ShowCpu = false, ShowMemory = false, ShowFans = false, PowerSumOnly = true, HiddenGpuIds = new() { "/gpu-nvidia/0" } };
var visible = Metrics.Build(new(now, 5, 10, 64), powerSensors, false, 3, now, visibility);
Check(!visible.Any(m => m.Id is "cpu-load" or "cpu-temp" or "ram" or "fan"), "CPU, RAM and fans must be independently hideable.");
Check(!visible.Any(m => m.Id.StartsWith("/gpu-nvidia/0-", StringComparison.Ordinal)), "Hiding a GPU must remove its load, temperature and VRAM together.");
Check(visible.Single(m => m.Id == "/gpu-nvidia/1-load").Label == "GPU2", "Hiding GPU1 must not rename GPU2.");
Check(visible.Where(m => m.Id.Contains("power", StringComparison.Ordinal)).Select(m => m.Id).SequenceEqual(new[] { "power-sum" }), "Sum-only mode must omit all individual watt cells.");
Check(visible.Last().Text == "265W", "Hidden components must still contribute to the complete power sum.");
visibility.ShowComponentPower = false;
Check(!Metrics.Build(null, powerSensors, false, 3, now, visibility).Any(m => m.Id.Contains("power", StringComparison.Ordinal)), "The master power switch must override sum-only mode.");
var serializedVisibility = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(visibility))!;
Check(!serializedVisibility.ShowCpu && !serializedVisibility.ShowMemory && !serializedVisibility.ShowFans && serializedVisibility.PowerSumOnly && serializedVisibility.HiddenGpuIds.Contains("/gpu-nvidia/0"), "Visibility choices must persist.");
var defaults = System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!;
Check(defaults.ShowCpu && defaults.ShowMemory && defaults.ShowFans && defaults.HiddenGpuIds.Count == 0 && !defaults.PowerSumOnly, "Old settings must keep all existing components visible.");
Console.WriteLine("PASS: independent visibility, stable GPU numbering, full power sum, master override and persistence.");
