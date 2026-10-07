using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RigPulse;

public sealed class SettingsWindow : Window
{
    public Settings Result { get; private set; }
    public SettingsWindow(Settings current, GpuFrame[] gpus)
    {
        Result = current; Title = "RigPulse settings"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "RigPulse", FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Read-only hardware monitoring · v" + typeof(Program).Assembly.GetName().Version?.ToString(3), Margin = new Thickness(0, 4, 0, 18) });
        Slider AddSlider(string label, double minimum, double maximum, double value)
        {
            panel.Children.Add(new TextBlock { Text = label });
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 6, 0, 14) }; panel.Children.Add(slider); return slider;
        }
        var font = AddSlider("Font size (9–20)", 9, 20, current.FontSize);
        var fans = AddSlider("Maximum motherboard fans (0–12)", 0, 12, current.MaxFans);
        var opacity = AddSlider("Opacity (20–100%)", 20, 100, current.Opacity * 100);
        panel.Children.Add(new TextBlock { Text = "Display" });
        var monitor = new ComboBox { ItemsSource = System.Windows.Forms.Screen.AllScreens.Select((s, i) => $"Display {i + 1} · {s.Bounds.Width}×{s.Bounds.Height}").ToArray(), SelectedIndex = Math.Min(current.Monitor, System.Windows.Forms.Screen.AllScreens.Length - 1), Margin = new Thickness(0, 6, 0, 14) }; panel.Children.Add(monitor);
        var integrated = new CheckBox { Content = "Show Intel integrated graphics", IsChecked = current.ShowIntegratedGpu, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(integrated);
        panel.Children.Add(new TextBlock { Text = "Visible components", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        CheckBox AddToggle(string text, bool enabled) {
            var toggle = new CheckBox { Content = text, IsChecked = enabled, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(toggle); return toggle;
        }
        var cpu = AddToggle("Show CPU (load and temperature)", current.ShowCpu);
        var memory = AddToggle("Show RAM", current.ShowMemory);
        var gpuToggles = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        int gpuIndex = 0;
        foreach (var gpu in gpus.Where(g => g.Type != "GpuIntel").OrderBy(g => g.DriverIndex ?? int.MaxValue).ThenBy(g => g.Id, StringComparer.Ordinal)) {
            var toggle = AddToggle("Show GPU" + ++gpuIndex + " · " + gpu.Name, !current.HiddenGpuIds.Contains(gpu.Id));
            toggle.ToolTip = gpu.Id; gpuToggles[gpu.Id] = toggle;
        }
        if (gpuToggles.Count == 0) panel.Children.Add(new TextBlock { Text = "GPU switches appear after sensors discover the cards.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var showFans = AddToggle("Show all motherboard fans", current.ShowFans);
        var power = new CheckBox { Content = "Show component power (CPU + discrete GPUs)", IsChecked = current.ShowComponentPower, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(power);
        var sumOnly = AddToggle("Show only the power sum", current.PowerSumOnly);
        panel.Children.Add(new TextBlock { Text = "When unchecked, individual watts appear when they fit; otherwise only the sum is shown automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        sumOnly.IsEnabled = power.IsChecked == true;
        power.Checked += (_, _) => sumOnly.IsEnabled = true;
        power.Unchecked += (_, _) => sumOnly.IsEnabled = false;
        panel.Children.Add(new TextBlock { Text = "The power sum always includes CPU and all discrete GPUs, even when their monitoring blocks are hidden.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var dock = new CheckBox { Content = "Full-width bar above taskbar", IsChecked = current.DockAboveTaskbar, Margin = new Thickness(0, 0, 0, 4) }; panel.Children.Add(dock);
        panel.Children.Add(new TextBlock { Text = "Reserve screen space so maximized and snapped windows end above the bar. Uncheck to keep the compact overlay.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var startup = new CheckBox { Content = "Start at Windows sign-in (current user)", IsChecked = current.StartAtLogin, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(startup);
        panel.Children.Add(new TextBlock { Text = "Missing temperatures or fans? Restart RigPulse as administrator from the tray menu. Sensor support depends on the hardware and driver. Fan labels can be edited in settings.json using sensor IDs from diagnostics.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Fan color references (RPM; display only)", FontWeight = FontWeights.Bold });
        TextBox AddRpm(string label, int value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            row.Children.Add(new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 80 }; row.Children.Add(input); panel.Children.Add(row); return input;
        }
        var cpuRpm = AddRpm("CPU fan reference", current.CpuFanMaxRpm);
        var systemRpm = AddRpm("System fan reference", current.SystemFanMaxRpm);
        panel.Children.Add(new TextBlock { Text = "Fan colors: yellow at 60%, orange at 80%, red at 95% of the reference. Red indicates high speed, not a fan fault. Per-sensor overrides: FanMaxRpm in settings.json.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) });
        var save = new Button { Content = "Save", Padding = new Thickness(15, 7, 15, 7), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => {
            if (!int.TryParse(cpuRpm.Text, out int cpuLimit) || !int.TryParse(systemRpm.Text, out int systemLimit) || cpuLimit is < 100 or > 30000 || systemLimit is < 100 or > 30000) { System.Windows.MessageBox.Show("Enter fan reference speeds between 100 and 30000 RPM.", "Invalid fan reference"); return; }
            Result = new Settings { FontSize = (int)font.Value, MaxFans = (int)fans.Value, Opacity = opacity.Value / 100,
                ShowCpu = cpu.IsChecked == true, ShowMemory = memory.IsChecked == true, ShowFans = showFans.IsChecked == true,
                PowerSumOnly = sumOnly.IsChecked == true, HiddenGpuIds = new(current.HiddenGpuIds, StringComparer.Ordinal),
                DockAboveTaskbar = dock.IsChecked == true, ShowComponentPower = power.IsChecked == true, Monitor = monitor.SelectedIndex, ShowIntegratedGpu = integrated.IsChecked == true, StartAtLogin = startup.IsChecked == true, FanLabels = new(current.FanLabels), CpuFanMaxRpm = cpuLimit, SystemFanMaxRpm = systemLimit, FanMaxRpm = new(current.FanMaxRpm) };
            foreach (var (id, toggle) in gpuToggles) { if (toggle.IsChecked == true) Result.HiddenGpuIds.Remove(id); else Result.HiddenGpuIds.Add(id); }
            try {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (Result.StartAtLogin) key.SetValue("RigPulse", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("RigPulse", false);
                DialogResult = true;
            } catch (Exception e) { System.Windows.MessageBox.Show(e.Message, "Could not update startup settings"); }
        };
        panel.Children.Add(save);
    }
}
