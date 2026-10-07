using System.Runtime.InteropServices;

namespace RigPulse;

[StructLayout(LayoutKind.Sequential)]
public struct DesktopRect : IEquatable<DesktopRect>
{
    public int Left, Top, Right, Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Equals(DesktopRect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    public override readonly bool Equals(object? value) => value is DesktopRect other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public readonly bool IsValidDock(DesktopRect bounds) => Width > 0 && Height > 0 &&
        Left >= bounds.Left && Right <= bounds.Right && Top >= bounds.Top && Bottom <= bounds.Bottom;
}

// All shell rectangles are physical pixels, including on mixed-DPI displays.
// Reserving via the shell (rather than changing SPI_SETWORKAREA) cooperates
// with the taskbar, other appbars, multiple monitors and automatic cleanup.
public sealed class DesktopDock : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint Callback, Edge;
        public DesktopRect Rect;
        public IntPtr Parameter;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public uint Size;
        public DesktopRect Bounds, WorkArea;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out DesktopRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private readonly IntPtr window;
    private readonly uint callback = RegisterWindowMessage("RigPulse.DesktopDock.Callback"), taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool registered, placing;
    private DesktopRect? reserved;
    public bool Registered => registered;
    public event Action? PositionChanged;
    public event Action<bool>? FullscreenChanged;
    public DesktopDock(IntPtr window) { this.window = window; }
    private AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = window, Callback = callback, Edge = 3 };
    public static DesktopRect WorkArea(DesktopRect bounds)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new Point { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }, 2);
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Windows could not read the display work area.");
        return info.WorkArea;
    }
    public static DesktopRect CurrentBounds(DesktopRect previous)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new Point { X = previous.Left + previous.Width / 2, Y = previous.Top + previous.Height / 2 }, 2);
        if (!GetMonitorInfo(monitor, ref info) || info.Bounds.Width <= 0 || info.Bounds.Height <= 0)
            throw new InvalidOperationException("The display configuration is still changing.");
        return info.Bounds;
    }
    public static DesktopRect WindowRect(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect)) throw new InvalidOperationException("Windows could not read the panel position.");
        return rect;
    }
    public DesktopRect Reserve(DesktopRect bounds, int height)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || height <= 0 || height > bounds.Height)
            throw new InvalidOperationException("The display configuration is still changing.");
        var data = Data();
        placing = true;
        try
        {
            if (!registered)
            {
                if (SHAppBarMessage(0, ref data) == UIntPtr.Zero) throw new InvalidOperationException("Windows could not dock the panel.");
                registered = true;
            }
            data.Rect = bounds; data.Rect.Top = bounds.Bottom - height;
            SHAppBarMessage(2, ref data); // ABM_QUERYPOS excludes the taskbar and other appbars.
            data.Rect.Top = data.Rect.Bottom - height;
            if (!data.Rect.IsValidDock(bounds)) throw new InvalidOperationException("Windows returned a transient invalid dock rectangle.");
            if (reserved is not DesktopRect previous || !previous.Equals(data.Rect))
            {
                SHAppBarMessage(3, ref data); // ABM_SETPOS can adjust the proposed rectangle again.
                if (!data.Rect.IsValidDock(bounds)) throw new InvalidOperationException("Windows returned a transient invalid dock rectangle.");
                reserved = data.Rect;
            }
            return reserved!.Value;
        }
        finally { placing = false; }
    }
    public void Move(DesktopRect rect)
    {
        if (WindowRect(window).Equals(rect)) return;
        placing = true;
        try
        {
            if (!SetWindowPos(window, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, 0x14))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { placing = false; }
    }
    public void HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if ((uint)message == taskbarCreated)
        {
            // Explorer forgot registrations after restarting; reclaim only our bar.
            registered = false; reserved = null; PositionChanged?.Invoke();
        }
        else if ((uint)message == callback && registered)
        {
            if (wParam.ToInt32() == 1 && !placing) PositionChanged?.Invoke(); // ABN_POSCHANGED
            else if (wParam.ToInt32() == 2) FullscreenChanged?.Invoke(lParam != IntPtr.Zero);
        }
        else if (registered && (message == 0x6 || message == 0x47))
        {
            var data = Data(); data.Parameter = wParam;
            SHAppBarMessage(message == 0x6 ? 6u : 9u, ref data); // ACTIVATE / WINDOWPOSCHANGED
        }
    }
    public void Dispose()
    {
        if (!registered) return;
        registered = false; reserved = null;
        var data = Data(); SHAppBarMessage(1, ref data); // ABM_REMOVE restores the work area.
    }
}
