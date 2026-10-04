using RigPulse;
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var now = DateTimeOffset.UtcNow;
var gpu = new GpuFrame("/gpu-nvidia/1", "Second NVIDIA GPU", "GpuNvidia", 1, 35, .4, 15.9);
var sensor = new SensorFrame(now, 32, [gpu, gpu with { Id = "/gpu-nvidia/0" }, gpu with { Id = "/gpu-intel/0", Type = "GpuIntel" }], [new("fan", "CPU Fan", 700)]);
var low = Metrics.Build(new(now, 1, 9.8, 63.7), sensor, false, 3, now);
var high = Metrics.Build(new(now, 100, 63.7, 63.7), sensor with { CpuTemperature = 100 }, false, 3, now);
Check(low.Select(m => (m.Id, m.Characters)).SequenceEqual(high.Select(m => (m.Id, m.Characters))), "Numeric changes must not change cell widths or topology.");
Check(low.Count(m => m.Label.StartsWith("GPU")) == 2, "Both discrete GPUs must be displayed, without Intel substitution.");
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
