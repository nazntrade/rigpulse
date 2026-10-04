using System.Globalization;
using System.Text.Json;

namespace RigPulse;

public record SystemFrame(DateTimeOffset Timestamp, double? CpuLoad, double MemoryUsed, double MemoryTotal);
public record GpuFrame(string Id, string Name, string Type, double? Load, double? Temperature, double? Used, double? Total, int? DriverIndex = null);
public record FanFrame(string Id, string Name, double? Rpm);
public record SensorFrame(DateTimeOffset Timestamp, double? CpuTemperature, GpuFrame[] Gpus, FanFrame[] Fans, string? Error = null);
public record Metric(string Id, string Label, int Characters, string Text, double? Level = null, string? Tooltip = null);

public static class Metrics
{
    public static string Number(double? value, string format, string suffix = "") =>
        value is double n && double.IsFinite(n) ? n.ToString(format, CultureInfo.InvariantCulture) + suffix : "--";
    public static string Memory(double? used, double? total) => used is not null && total is > 0
        ? Number(used, "0.0") + "/" + Number(total, "0.0") + "G" : "--";
    public static bool Fresh(DateTimeOffset? timestamp, DateTimeOffset now, double seconds = 5) =>
        timestamp is not null && (now - timestamp.Value).TotalSeconds <= seconds && timestamp <= now.AddSeconds(1);
    public static List<Metric> Build(SystemFrame? system, SensorFrame? sensors, bool showIntel, int maxFans, DateTimeOffset now)
    {
        bool fast = Fresh(system?.Timestamp, now), slow = Fresh(sensors?.Timestamp, now);
        var result = new List<Metric> {
            new("cpu-load", "CPU", 4, Number(fast ? system?.CpuLoad : null, "0", "%"), fast ? system?.CpuLoad : null, "Windows CPU utilization"),
            new("cpu-temp", "", 5, Number(slow ? sensors?.CpuTemperature : null, "0", "°C"), slow ? sensors?.CpuTemperature : null, "CPU temperature"),
            new("ram", "RAM", 12, fast ? Memory(system?.MemoryUsed, system?.MemoryTotal) : "--", fast && system is not null && system.MemoryTotal > 0 ? system.MemoryUsed / system.MemoryTotal * 100 : null, "Physical RAM used / total (GiB)")
        };
        int index = 0;
        foreach (var gpu in (sensors?.Gpus ?? []).Where(g => showIntel || g.Type != "GpuIntel").OrderBy(g => g.DriverIndex ?? int.MaxValue).ThenBy(g => g.Id, StringComparer.Ordinal))
        {
            string label = gpu.Type == "GpuIntel" ? "iGPU" : "GPU" + ++index;
            result.Add(new(gpu.Id + "-load", label, 4, Number(slow ? gpu.Load : null, "0", "%"), slow ? gpu.Load : null, gpu.Name + " · " + gpu.Id));
            result.Add(new(gpu.Id + "-temp", "", 5, Number(slow ? gpu.Temperature : null, "0", "°C"), slow ? gpu.Temperature : null, gpu.Name));
            result.Add(new(gpu.Id + "-memory", "VRAM", 12, slow ? Memory(gpu.Used, gpu.Total) : "--", slow && gpu.Total is > 0 ? gpu.Used / gpu.Total * 100 : null, gpu.Name + " · dedicated memory used / total (GiB)"));
        }
        foreach (var fan in sensors?.Fans.Take(maxFans) ?? [])
            result.Add(new(fan.Id, fan.Name, 6, Number(slow ? fan.Rpm : null, "0"), null, "Read-only fan speed (RPM) · " + fan.Id));
        result.Add(new("status", "", 9, sensors is null ? "Starting" : !slow ? "Stale" : sensors.Error is not null ? "Partial" : "", null,
            sensors?.Error ?? "Unavailable or stale sensors show --. CPU/RAM polling is independent."));
        return result;
    }
}

public class Settings
{
    public double Opacity { get; set; } = .82;
    public int FontSize { get; set; } = 12;
    public int MaxFans { get; set; } = 3;
    public int Monitor { get; set; } = 0;
    public bool ShowIntegratedGpu { get; set; }
    public bool StartAtLogin { get; set; }
    public Dictionary<string, string> FanLabels { get; set; } = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigPulse");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); s.Normalize(); return s; }
        catch { return new(); }
    }
    public void Normalize()
    {
        if (!double.IsFinite(Opacity)) Opacity = .82;
        Opacity = Math.Clamp(Opacity, .2, 1); FontSize = Math.Clamp(FontSize, 9, 20);
        MaxFans = Math.Clamp(MaxFans, 0, 12); Monitor = Math.Clamp(Monitor, 0, 32);
        FanLabels ??= new();
    }
    public void Save()
    {
        Normalize(); Directory.CreateDirectory(DirectoryPath);
        string pending = FilePath + ".new";
        File.WriteAllText(pending, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(pending, FilePath, true);
    }
}
