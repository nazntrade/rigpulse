using System.Runtime.InteropServices;
using System.Text;

namespace RigPulse;

// NVIDIA's driver-supplied NVML library. No extra application or shell process is used.
public sealed class NvidiaSensors : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfoV2 { public uint Version; public ulong Total, Reserved, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlShutdown();
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetUUID(IntPtr device, StringBuilder uuid, uint length);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out MemoryInfo info);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetMemoryInfo_v2(IntPtr device, ref MemoryInfoV2 info);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensor, out uint temperature);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    private readonly bool initialized;
    public bool Available => initialized;
    public NvidiaSensors() { try { initialized = nvmlInit_v2() == 0; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } }
    public GpuFrame[] Read()
    {
        if (!initialized || nvmlDeviceGetCount_v2(out var count) != 0) return [];
        var result = new List<GpuFrame>();
        for (uint i = 0; i < Math.Min(count, 32); i++)
        {
            if (nvmlDeviceGetHandleByIndex_v2(i, out var device) != 0) continue;
            var name = new StringBuilder(96); var uuid = new StringBuilder(96);
            nvmlDeviceGetName(device, name, 96);
            if (nvmlDeviceGetUUID(device, uuid, 96) != 0) continue;
            double? temperature = nvmlDeviceGetTemperature(device, 0, out var temp) == 0 ? temp : null;
            double? load = nvmlDeviceGetUtilizationRates(device, out var usage) == 0 ? usage.Gpu : null;
            double? power = null;
            try { if (nvmlDeviceGetPowerUsage(device, out var milliwatts) == 0) power = milliwatts / 1000d; } catch (EntryPointNotFoundException) { }
            double? used = null, total = null;
            var mem2 = new MemoryInfoV2 { Version = (uint)Marshal.SizeOf<MemoryInfoV2>() | (2u << 24) };
            try { if (nvmlDeviceGetMemoryInfo_v2(device, ref mem2) == 0) { used = mem2.Used / 1073741824d; total = mem2.Total / 1073741824d; } }
            catch (EntryPointNotFoundException) { }
            if (total is null && nvmlDeviceGetMemoryInfo(device, out var mem) == 0) { used = mem.Used / 1073741824d; total = mem.Total / 1073741824d; }
            result.Add(new("/gpu-nvidia/" + uuid, name.ToString(), "GpuNvidia", load, temperature,
                used, total, (int)i, power));
        }
        return result.ToArray();
    }
    public void Dispose() { if (initialized) nvmlShutdown(); }
}
