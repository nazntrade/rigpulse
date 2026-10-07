using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RigPulse;

public sealed class OverlayWindow : Window
{
    private Settings settings = Settings.Load();
    private readonly WrapPanel strip = new();
    private readonly Border surface = new();
    private readonly Dictionary<string, TextBlock> values = new();
    private readonly Dictionary<string, double> measuredTextWidths = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly WorkerHost<SystemFrame>? system;
    private readonly WorkerHost<SensorFrame>? sensors;
    private readonly bool demo;
    private readonly bool peakDemo;
    private static Brush ColorBrush(byte red, byte green, byte blue) { var brush = new SolidColorBrush(Color.FromRgb(red, green, blue)); brush.Freeze(); return brush; }
    private static readonly Brush greenBrush = ColorBrush(166, 255, 111), yellowBrush = ColorBrush(255, 230, 109), orangeBrush = ColorBrush(255, 166, 64), redBrush = ColorBrush(255, 101, 101);
    private string topology = "";
    private bool paused;
    private int tick;
    private HashSet<string>? selectedFans;
    private GpuFrame[] availableGpus = [];
    private DesktopDock? dock;
    private bool dockDirty = true, positioning, positionQueued, closing;
    private double compactWidth;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    public OverlayWindow(bool demo, bool peakDemo = false)
    {
        this.demo = demo; this.peakDemo = peakDemo;
        Title = "RigPulse"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight;
        surface.Background = new SolidColorBrush(Color.FromRgb(16, 22, 17));
        surface.CornerRadius = new CornerRadius(8); surface.Padding = new Thickness(10, 6, 10, 6); surface.Child = strip;
        Content = surface;
        if (!demo) { system = new("--system-worker"); sensors = new("--sensor-worker"); }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("Licenses", null, (_, _) => Dispatcher.Invoke(OpenLicenses));
        menu.Items.Add("Restart sensors", null, (_, _) => { system?.Restart(); sensors?.Restart(); });
        menu.Items.Add("Pause / resume", null, (_, _) => paused = !paused);
        menu.Items.Add("Open settings folder", null, (_, _) => { Directory.CreateDirectory(Settings.DirectoryPath); Process.Start(new ProcessStartInfo(Settings.DirectoryPath) { UseShellExecute = true }); });
        menu.Items.Add("Restart as administrator", null, (_, _) => {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" }); Close(); }
            catch (Exception e) { Program.Log(e); }
        });
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        using (var iconStream = typeof(Program).Assembly.GetManifestResourceStream("RigPulse.Icon")!)
        using (var embeddedIcon = new System.Drawing.Icon(iconStream)) trayIcon = (System.Drawing.Icon)embeddedIcon.Clone();
        tray = new Forms.NotifyIcon { Text = "RigPulse · hardware monitor", Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        MouseRightButtonUp += (_, _) => menu.Show(Forms.Cursor.Position);
        SourceInitialized += (_, _) => {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
            dock = new DesktopDock(hwnd);
            dock.PositionChanged += QueuePosition;
            dock.FullscreenChanged += fullscreen => { if (settings.DockAboveTaskbar) Topmost = !fullscreen; };
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
        };
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        SizeChanged += (_, _) => Position();
        timer.Tick += (_, _) => Refresh();
        Closing += (_, _) => { closing = true; dock?.Dispose(); };
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); trayIcon.Dispose(); system?.Dispose(); sensors?.Dispose(); };
    }
    private void Refresh()
    {
        if (paused) return;
        system?.Check(); sensors?.Check();
        var now = DateTimeOffset.UtcNow;
        if (demo) tick++;
        var fast = demo ? new SystemFrame(now, (peakDemo || tick % 2 == 0) ? 100 : 2, (peakDemo || tick % 2 == 0) ? 60 : 9.8, 63.7) : system?.Latest;
        var slow = demo ? new SensorFrame(now, (peakDemo || tick % 2 == 0) ? 100 : 32,
            [new("/gpu-nvidia/0", "NVIDIA GPU 1", "GpuNvidia", (peakDemo || tick % 2 == 0) ? 100 : 0, peakDemo ? 86 : 38, peakDemo ? 15.2 : .4, 15.9), new("/gpu-nvidia/1", "NVIDIA GPU 2", "GpuNvidia", peakDemo ? 100 : 0, peakDemo ? 86 : 35, peakDemo ? 15.2 : .4, 15.9)],
            [new("/fan/0", "CPU Fan", (peakDemo || tick % 2 == 0) ? 1818 : 699), new("/fan/1", "SYS Fan #1", peakDemo ? 1263 : 708), new("/fan/2", "SYS Fan #2", peakDemo ? 1294 : 700)]) : sensors?.Latest;
        if (demo && slow is not null) slow = slow with { CpuPowerWatts = (peakDemo || tick % 2 == 0) ? 228 : 8, Gpus = slow.Gpus.Select(g => g with { PowerWatts = (peakDemo || tick % 2 == 0) ? 180 : 7 }).ToArray() };
        if (slow is not null) availableGpus = slow.Gpus;
        if (slow is not null && selectedFans is null && slow.Fans.Any(f => f.Rpm is > 0))
            selectedFans = slow.Fans.Where(f => f.Rpm is > 0).Take(settings.MaxFans).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        if (slow is not null && selectedFans is not null) slow = slow with { Fans = slow.Fans.Where(f => selectedFans.Contains(f.Id)).ToArray() };
        var metrics = Metrics.Build(fast, slow, settings.ShowIntegratedGpu, settings.MaxFans, now, settings);
        if (demo) metrics = metrics.Select(m => m.Id == "status" ? m with { Text = "Demo" } : m).ToList();
        metrics = metrics.Select(m => settings.FanLabels.TryGetValue(m.Id, out var label) ? m with { Label = label.Length > 18 ? label[..18] : label } : m with { Label = m.Label.Replace("System Fan", "SYS Fan", StringComparison.Ordinal) }).ToList();
        metrics = metrics.Where(m => m.Id != "status" || !string.IsNullOrEmpty(m.Text)).ToList();
        bool HasSeparator(Metric metric) => metric != metrics[0] && (metric.Id == "power-cpu" || metric.Id.EndsWith("-power", StringComparison.Ordinal) || metric.Id == "power-sum" || metric.Id == "ram" || metric.Id.EndsWith("-load", StringComparison.Ordinal) && metric.Id != "cpu-load" || selectedFans?.Contains(metric.Id) == true);
        bool HasMetricDot(Metric metric) => metric.Id.EndsWith("-memory", StringComparison.Ordinal);
        var layoutScreen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
        var layoutDpi = VisualTreeHelper.GetDpi(this);
        double TextWidth(string text) {
            string cacheKey = settings.FontSize + ":" + layoutDpi.PixelsPerDip + ":" + text;
            if (!measuredTextWidths.TryGetValue(cacheKey, out double width)) {
                var block = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = settings.FontSize, FontWeight = FontWeights.Bold };
                block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                measuredTextWidths[cacheKey] = width = block.DesiredSize.Width;
            }
            return width;
        }
        double requiredWidth = metrics.Sum(m => Math.Ceiling(m.Characters * settings.FontSize * .56) +
            (m.Label.Length > 0 ? TextWidth(m.Label + " ") : 0) + 5 + (HasSeparator(m) ? 14 : 0) +
            (HasMetricDot(m) ? TextWidth("·") + 5 : 0));
        double availableWidth = layoutScreen.WorkingArea.Width / layoutDpi.DpiScaleX - (settings.DockAboveTaskbar ? 16 : 44);
        // Decide from fixed cell widths and the display, never from changing readings
        // or the already-collapsed panel width. A wider display restores details.
        metrics = Metrics.FitPowerBlock(metrics, requiredWidth, availableWidth);
        string key = string.Join("|", metrics.Select(m => m.Id + ":" + m.Label));
        if (key != topology)
        {
            topology = key; strip.Children.Clear(); values.Clear();
            dockDirty = true;
            foreach (var m in metrics)
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 5, 0) };
                if (HasSeparator(m)) cell.Children.Add(new Border {
                    Width = 1, Height = settings.FontSize + 2, Background = Brushes.Gray,
                    Margin = new Thickness(4, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center
                });
                if (HasMetricDot(m)) cell.Children.Add(new TextBlock {
                    Text = "·", Foreground = Brushes.Gray, FontFamily = new FontFamily("Consolas"),
                    FontSize = settings.FontSize, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 5, 0)
                });
                if (m.Label.Length > 0) cell.Children.Add(new TextBlock { Text = m.Label + " ", Foreground = Brushes.LightGray, FontFamily = new FontFamily("Consolas"), FontSize = settings.FontSize, FontWeight = FontWeights.Bold });
                var value = new TextBlock { Width = Math.Ceiling(m.Characters * settings.FontSize * .56), TextAlignment = m.Id == "ram" || m.Id.EndsWith("-memory", StringComparison.Ordinal) ? TextAlignment.Left : TextAlignment.Right,
                    FontFamily = new FontFamily("Consolas"), FontSize = settings.FontSize, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
                values[m.Id] = value; cell.Children.Add(value); strip.Children.Add(cell);
            }
            double total = metrics.Sum(m => (m.Characters + (m.Label.Length > 0 ? m.Label.Length + 1 : 0)) * settings.FontSize * .56 + 5 + (HasSeparator(m) ? 14 : 0) + (HasMetricDot(m) ? settings.FontSize * .56 + 5 : 0)) + 20;
            var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            compactWidth = Math.Ceiling(total);
            if (!settings.DockAboveTaskbar) surface.Width = Math.Min(compactWidth, screen.WorkingArea.Width / scale - 24);
        }
        foreach (var m in metrics)
        {
            values[m.Id].Text = m.Text; values[m.Id].ToolTip = m.Tooltip;
            values[m.Id].Foreground = Metrics.Tone(m) switch {
                MetricTone.Critical => redBrush, MetricTone.High => orangeBrush, MetricTone.Medium => yellowBrush,
                MetricTone.Low => greenBrush, _ => Brushes.Gray
            };
        }
        Opacity = settings.Opacity; Position();
    }
    private void Position()
    {
        if (closing || positioning) return;
        var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
        var bounds = new DesktopRect { Left = screen.Bounds.Left, Top = screen.Bounds.Top, Right = screen.Bounds.Right, Bottom = screen.Bounds.Bottom };
        var dpi = VisualTreeHelper.GetDpi(this);
        if (settings.DockAboveTaskbar && dock is not null)
        {
            if (!dockDirty && dock.Registered) return;
            positioning = true;
            try
            {
                SizeToContent = SizeToContent.Manual;
                surface.CornerRadius = new CornerRadius(0); surface.Padding = new Thickness(8, 2, 8, 2);
                strip.Measure(new Size(Math.Max(1, bounds.Width / dpi.DpiScaleX - 16), double.PositiveInfinity));
                int height = (int)Math.Ceiling((strip.DesiredSize.Height + 4) * dpi.DpiScaleY);
                var rect = dock.Reserve(bounds, height);
                strip.Measure(new Size(Math.Max(1, rect.Width / dpi.DpiScaleX - 16), double.PositiveInfinity));
                int fittedHeight = (int)Math.Ceiling((strip.DesiredSize.Height + 4) * dpi.DpiScaleY);
                if (fittedHeight != height) rect = dock.Reserve(bounds, fittedHeight);
                Width = rect.Width / dpi.DpiScaleX; Height = rect.Height / dpi.DpiScaleY;
                surface.Width = Width;
                dock.Move(rect);
                dockDirty = false;
            }
            catch (Exception error)
            {
                dock.Dispose(); settings.DockAboveTaskbar = false; Program.Log(error);
                Dispatcher.BeginInvoke(() => { Position(); System.Windows.MessageBox.Show(error.Message, "Could not dock RigPulse"); });
            }
            finally { positioning = false; }
            return;
        }
        dock?.Dispose(); Topmost = true;
        surface.CornerRadius = new CornerRadius(8); surface.Padding = new Thickness(10, 6, 10, 6);
        Width = double.NaN; Height = double.NaN; SizeToContent = SizeToContent.WidthAndHeight;
        var work = DesktopDock.WorkArea(bounds);
        if (compactWidth > 0) surface.Width = Math.Min(compactWidth, work.Width / dpi.DpiScaleX - 24);
        Left = work.Left / dpi.DpiScaleX + 12;
        Top = work.Bottom / dpi.DpiScaleY - ActualHeight - 2;
    }
    private void QueuePosition()
    {
        dockDirty = true;
        if (positionQueued || closing) return;
        positionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { positionQueued = false; dockDirty = true; Position(); });
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        dock?.HandleMessage(message, wParam, lParam);
        if (message is 0x1A or 0x7E or 0x2E0) QueuePosition();
        return IntPtr.Zero;
    }
    internal void SetDockedForTest(bool enabled)
    {
        settings.DockAboveTaskbar = enabled; dockDirty = true; Position();
    }
    internal object LayoutSnapshot()
    {
        var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
        var bounds = new DesktopRect { Left = screen.Bounds.Left, Top = screen.Bounds.Top, Right = screen.Bounds.Right, Bottom = screen.Bounds.Bottom };
        var rect = DesktopDock.WindowRect(new WindowInteropHelper(this).Handle);
        var work = DesktopDock.WorkArea(bounds);
        return new { docked = settings.DockAboveTaskbar, registered = dock?.Registered, monitor = settings.Monitor,
            left = rect.Left, top = rect.Top, width = rect.Width, height = rect.Height,
            workLeft = work.Left, workTop = work.Top, workRight = work.Right, workBottom = work.Bottom,
            fontSize = settings.FontSize, padding = surface.Padding.Top, cornerRadius = surface.CornerRadius.TopLeft,
            valueWidths = values.ToDictionary(p => p.Key, p => p.Value.Width) };
    }
    internal object WorkAreaSnapshot()
    {
        var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
        var work = DesktopDock.WorkArea(new DesktopRect { Left = screen.Bounds.Left, Top = screen.Bounds.Top, Right = screen.Bounds.Right, Bottom = screen.Bounds.Bottom });
        return new { registered = dock?.Registered, workLeft = work.Left, workTop = work.Top, workRight = work.Right, workBottom = work.Bottom };
    }
    private void OpenSettings()
    {
        var window = new SettingsWindow(settings, availableGpus);
        if (window.ShowDialog() == true) { settings = window.Result; settings.Save(); topology = ""; selectedFans = null; dockDirty = true; Refresh(); }
    }
    private void OpenLicenses()
    {
        var assembly = typeof(Program).Assembly;
        var text = string.Join("\n\n", assembly.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal).Select(n => {
            using var stream = assembly.GetManifestResourceStream(n)!;
            using var reader = new StreamReader(stream);
            return "=== " + n + " ===\n" + reader.ReadToEnd();
        }));
        new Window { Title = "RigPulse · licenses", Width = 780, Height = 600, Content = new TextBox {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12)
        } }.ShowDialog();
    }
}
