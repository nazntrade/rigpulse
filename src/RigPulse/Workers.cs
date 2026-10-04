using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace RigPulse;

public sealed class WindowsCounters
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private ulong idleBefore, totalBefore;
    public SystemFrame Read()
    {
        double? load = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value + user.Value;
            if (totalBefore > 0 && total > totalBefore && idle.Value >= idleBefore)
                load = Math.Clamp(100d * (1 - (double)(idle.Value - idleBefore) / (total - totalBefore)), 0, 100);
            idleBefore = idle.Value; totalBefore = total;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Windows memory counters are unavailable.");
        return new(DateTimeOffset.UtcNow, load, (memory.Total - memory.Available) / 1073741824d, memory.Total / 1073741824d);
    }
}

public static class Workers
{
    private static double? Finite(double? value) => value is double n && double.IsFinite(n) ? n : null;
    private static int stopping;
    private static void ListenForStop() => System.Threading.Tasks.Task.Run(() => { try { Console.ReadLine(); } catch { } Interlocked.Exchange(ref stopping, 1); });
    public static void SystemLoop()
    {
        ListenForStop();
        var counters = new WindowsCounters();
        while (Volatile.Read(ref stopping) == 0) { Console.WriteLine(JsonSerializer.Serialize(counters.Read())); Thread.Sleep(1000); }
    }
    public static void SensorLoop(bool simulateHang)
    {
        if (simulateHang) Thread.Sleep(Timeout.Infinite);
        ListenForStop();
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
        computer.Open();
        using var nvidia = new NvidiaSensors();
        try { while (Volatile.Read(ref stopping) == 0)
        {
            var hardware = new List<IHardware>(); var errors = new List<string>();
            void Visit(IHardware h) { try { h.Update(); hardware.Add(h); foreach (var child in h.SubHardware) Visit(child); } catch (Exception e) { errors.Add(e.Message); } }
            foreach (var h in computer.Hardware) Visit(h);
            double? Pick(IHardware h, SensorType type, params string[] names) => Finite(h.Sensors.FirstOrDefault(s => s.SensorType == type && names.Contains(s.Name))?.Value);
            double? Memory(IHardware h, string name)
            {
                var small = Pick(h, SensorType.SmallData, name);
                return small is not null ? small / 1024 : Pick(h, SensorType.Data, name);
            }
            var cpu = hardware.Where(h => h.HardwareType == HardwareType.Cpu).SelectMany(h => h.Sensors)
                .Where(s => s.SensorType == SensorType.Temperature && Finite(s.Value) is not null).ToArray();
            double? cpuTemp = cpu.FirstOrDefault(s => s.Name.Contains("Package"))?.Value ??
                (cpu.Length > 0 ? cpu.Max(s => (double?)s.Value) : null);
            var gpuFrames = hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                .Select(h => new GpuFrame(h.Identifier.ToString(), h.Name, h.HardwareType.ToString(),
                    Pick(h, SensorType.Load, "GPU Core", "GPU D3D 3D", "GPU"), Pick(h, SensorType.Temperature, "GPU Core", "GPU"),
                    h.HardwareType == HardwareType.GpuNvidia ? Memory(h, "D3D Dedicated Memory Used") : Memory(h, "GPU Memory Used"), Memory(h, "GPU Memory Total"))).ToArray();
            var nativeNvidia = nvidia.Read();
            var gpus = nativeNvidia.Length > 0 ? gpuFrames.Where(g => g.Type != "GpuNvidia").Concat(nativeNvidia).ToArray() : gpuFrames;
            var fans = hardware.Where(h => !h.HardwareType.ToString().StartsWith("Gpu", StringComparison.Ordinal))
                .SelectMany(h => h.Sensors.Where(s => s.SensorType == SensorType.Fan)
                    .Select(s => new FanFrame(s.Identifier.ToString(), s.Name, Finite(s.Value)))).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new SensorFrame(DateTimeOffset.UtcNow, cpuTemp, gpus, fans, errors.Count > 0 ? string.Join("; ", errors.Distinct()) : null)));
            Thread.Sleep(1000);
        } } finally { computer.Close(); }
    }
}

public sealed class WorkerHost<T> : IDisposable where T : class
{
    private readonly string mode;
    private Process? process;
    private long received;
    private T? latest;
    private int disposed;
    public T? Latest => Volatile.Read(ref latest);
    public object? Info => process is { HasExited: false } worker ? new { id = worker.Id, priority = worker.PriorityClass.ToString() } : null;
    public WorkerHost(string mode) { this.mode = mode; Start(); }
    private void Start()
    {
        Interlocked.Exchange(ref received, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var start = new ProcessStartInfo(Environment.ProcessPath!, mode) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => {
            if (e.Data is null || e.Data.Length > 131072) return;
            try { var frame = JsonSerializer.Deserialize<T>(e.Data); if (frame is not null) { Volatile.Write(ref latest, frame); Interlocked.Exchange(ref received, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); } } catch { }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        try { Scheduling.Configure(process); } catch (Exception e) { Program.Log(e); }
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
    }
    public void Check()
    {
        if (disposed != 0) return;
        // Keep the last topology and replace stale numbers with --; never remove the bar.
        if (process is null || process.HasExited || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Interlocked.Read(ref received) > 20000) Restart();
    }
    public void Restart() { Stop(); if (disposed == 0) Start(); }
    private void Stop(bool wait = false)
    {
        var owned = process; process = null;
        if (owned is null) return;
        void Finish() {
            try { if (!owned.HasExited) { owned.StandardInput.WriteLine("stop"); owned.StandardInput.Flush(); if (!owned.WaitForExit(1500)) owned.Kill(); } } catch { try { if (!owned.HasExited) owned.Kill(); } catch { } }
            finally { owned.Dispose(); }
        }
        if (wait) Finish(); else System.Threading.Tasks.Task.Run(Finish);
    }
    public void Dispose() { Interlocked.Exchange(ref disposed, 1); Stop(true); }
}
