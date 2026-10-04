using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RigPulse;

public sealed class SettingsWindow : Window
{
    public Settings Result { get; private set; }
    public SettingsWindow(Settings current)
    {
        Result = current; Title = "RigPulse settings"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "RigPulse", FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Read-only hardware monitoring · v0.1.1", Margin = new Thickness(0, 4, 0, 18) });
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
        var startup = new CheckBox { Content = "Start at Windows sign-in (current user)", IsChecked = current.StartAtLogin, Margin = new Thickness(0, 0, 0, 16) }; panel.Children.Add(startup);
        panel.Children.Add(new TextBlock { Text = "Missing temperatures or fans? Restart RigPulse as administrator from the tray menu. Sensor support depends on the hardware and driver. Fan labels can be edited in settings.json using sensor IDs from diagnostics.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var save = new Button { Content = "Save", Padding = new Thickness(15, 7, 15, 7), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) => {
            Result = new Settings { FontSize = (int)font.Value, MaxFans = (int)fans.Value, Opacity = opacity.Value / 100,
                Monitor = monitor.SelectedIndex, ShowIntegratedGpu = integrated.IsChecked == true, StartAtLogin = startup.IsChecked == true, FanLabels = new(current.FanLabels) };
            try {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (Result.StartAtLogin) key.SetValue("RigPulse", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("RigPulse", false);
                DialogResult = true;
            } catch (Exception e) { System.Windows.MessageBox.Show(e.Message, "Could not update startup settings"); }
        };
        panel.Children.Add(save);
    }
}
