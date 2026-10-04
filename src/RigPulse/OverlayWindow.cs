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
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon tray;
    private readonly WorkerHost<SystemFrame>? system;
    private readonly WorkerHost<SensorFrame>? sensors;
    private readonly bool demo;
    private string topology = "";
    private bool paused;
    private int tick;
    private HashSet<string>? selectedFans;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    public OverlayWindow(bool demo)
    {
        this.demo = demo;
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
        tray = new Forms.NotifyIcon { Text = "RigPulse · hardware monitor", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        MouseRightButtonUp += (_, _) => menu.Show(Forms.Cursor.Position);
        SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(this).Handle; SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); };
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        SizeChanged += (_, _) => Position();
        timer.Tick += (_, _) => Refresh();
        Closed += (_, _) => { timer.Stop(); tray.Dispose(); system?.Dispose(); sensors?.Dispose(); };
    }
    private void Refresh()
    {
        if (paused) return;
        system?.Check(); sensors?.Check();
        var now = DateTimeOffset.UtcNow;
        var fast = demo ? new SystemFrame(now, ++tick % 2 == 0 ? 100 : 2, tick % 2 == 0 ? 60 : 9.8, 63.7) : system?.Latest;
        var slow = demo ? new SensorFrame(now, tick % 2 == 0 ? 100 : 32,
            [new("/gpu-nvidia/0", "NVIDIA GPU 1", "GpuNvidia", tick % 2 == 0 ? 100 : 0, 38, .4, 15.9), new("/gpu-nvidia/1", "NVIDIA GPU 2", "GpuNvidia", 0, 35, .4, 15.9)],
            [new("/fan/0", "CPU Fan", tick % 2 == 0 ? 1600 : 699), new("/fan/1", "SYS Fan #1", 708), new("/fan/2", "SYS Fan #2", 700)]) : sensors?.Latest;
        if (slow is not null && selectedFans is null && slow.Fans.Any(f => f.Rpm is > 0))
            selectedFans = slow.Fans.Where(f => f.Rpm is > 0).Take(settings.MaxFans).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        if (slow is not null && selectedFans is not null) slow = slow with { Fans = slow.Fans.Where(f => selectedFans.Contains(f.Id)).ToArray() };
        var metrics = Metrics.Build(fast, slow, settings.ShowIntegratedGpu, settings.MaxFans, now);
        if (demo) metrics = metrics.Select(m => m.Id == "status" ? m with { Text = "Demo" } : m).ToList();
        metrics = metrics.Select(m => settings.FanLabels.TryGetValue(m.Id, out var label) ? m with { Label = label.Length > 18 ? label[..18] : label } : m with { Label = m.Label.Replace("System Fan", "SYS Fan", StringComparison.Ordinal) }).ToList();
        string key = string.Join("|", metrics.Select(m => m.Id + ":" + m.Label));
        if (key != topology)
        {
            topology = key; strip.Children.Clear(); values.Clear();
            bool HasSeparator(Metric metric) => metric.Id == "ram" || metric.Id.EndsWith("-load", StringComparison.Ordinal) && metric.Id != "cpu-load" || selectedFans?.Contains(metric.Id) == true;
            bool HasMetricDot(Metric metric) => metric.Id.EndsWith("-memory", StringComparison.Ordinal);
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
                var value = new TextBlock { Width = Math.Ceiling(m.Characters * settings.FontSize * .56), TextAlignment = TextAlignment.Right,
                    FontFamily = new FontFamily("Consolas"), FontSize = settings.FontSize, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
                values[m.Id] = value; cell.Children.Add(value); strip.Children.Add(cell);
            }
            double total = metrics.Sum(m => (m.Characters + (m.Label.Length > 0 ? m.Label.Length + 1 : 0)) * settings.FontSize * .56 + 5 + (HasSeparator(m) ? 14 : 0) + (HasMetricDot(m) ? settings.FontSize * .56 + 5 : 0)) + 20;
            var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            surface.Width = Math.Min(Math.Ceiling(total), screen.WorkingArea.Width / scale - 24);
        }
        foreach (var m in metrics)
        {
            values[m.Id].Text = m.Text; values[m.Id].ToolTip = m.Tooltip;
            values[m.Id].Foreground = m.Level >= 80 ? new SolidColorBrush(Color.FromRgb(255, 166, 64))
                : m.Level >= 60 ? new SolidColorBrush(Color.FromRgb(255, 230, 109)) : new SolidColorBrush(Color.FromRgb(166, 255, 111));
        }
        Opacity = settings.Opacity; Position();
    }
    private void Position()
    {
        var screen = Forms.Screen.AllScreens[Math.Min(settings.Monitor, Forms.Screen.AllScreens.Length - 1)];
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screen.WorkingArea.Left / dpi.DpiScaleX + 12;
        Top = screen.WorkingArea.Bottom / dpi.DpiScaleY - ActualHeight - 2;
    }
    private void OpenSettings()
    {
        var window = new SettingsWindow(settings);
        if (window.ShowDialog() == true) { settings = window.Result; settings.Save(); topology = ""; selectedFans = null; Refresh(); }
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
