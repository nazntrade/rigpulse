using System.Diagnostics;

namespace RigPulse;

public static class Scheduling
{
    // Task Scheduler's default priority is BelowNormal and children inherit it.
    // These short, periodic reads must still run when every CPU is busy.
    public static void Configure(Process process)
    {
        if (OperatingSystem.IsWindows()) process.PriorityClass = ProcessPriorityClass.AboveNormal;
    }
    public static void ConfigureCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        Configure(process);
    }
}
