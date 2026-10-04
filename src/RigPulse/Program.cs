using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RigPulse;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            try { Scheduling.ConfigureCurrentProcess(); } catch (Exception e) { Log(e); }
            if (args.Contains("--system-worker")) { Workers.SystemLoop(); return; }
            if (args.Contains("--sensor-worker")) { Workers.SensorLoop(args.Contains("--simulate-sensor-hang")); return; }
            if (args.Contains("--diagnostics"))
            {
                using var fast = new WorkerHost<SystemFrame>("--system-worker");
                using var slow = new WorkerHost<SensorFrame>("--sensor-worker" + (args.Contains("--simulate-sensor-hang") ? " --simulate-sensor-hang" : ""));
                int durationAt = Array.IndexOf(args, "--duration");
                int duration = durationAt >= 0 && durationAt + 1 < args.Length && int.TryParse(args[durationAt + 1], out int seconds) ? Math.Clamp(seconds, 2, 120) : 8;
                var samples = new List<object>();
                for (int i = 0; i < duration; i++) {
                    Thread.Sleep(1000); fast.Check(); slow.Check();
                    var capturedAt = DateTimeOffset.UtcNow;
                    samples.Add(new { capturedAt, system = fast.Latest, sensors = slow.Latest,
                        processPriority = Process.GetCurrentProcess().PriorityClass.ToString(),
                        systemWorker = fast.Info, sensorWorker = slow.Info });
                }
                int pathAt = Array.IndexOf(args, "--output");
                File.WriteAllText(pathAt >= 0 ? args[pathAt + 1] : Path.Combine(Path.GetTempPath(), "rigpulse-diagnostics.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            using var mutex = new Mutex(false, "Local\\RigPulse-Overlay");
            if (!mutex.WaitOne(5000)) return;
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
            var window = new OverlayWindow(args.Contains("--demo") || args.Contains("--layout-test"), args.Contains("--peak-demo"));
            if (args.Contains("--layout-test"))
            {
                int outputAt = Array.IndexOf(args, "--output");
                string output = outputAt >= 0 ? args[outputAt + 1] : Path.Combine(Path.GetTempPath(), "rigpulse-demo.png");
                var widths = new List<double>();
                var captureTimesSeconds = new List<double>();
                var captureClock = Stopwatch.StartNew();
                var capture = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                capture.Tick += (_, _) => {
                    widths.Add(window.ActualWidth);
                    captureTimesSeconds.Add(captureClock.Elapsed.TotalSeconds);
                    if (widths.Count < 6) return;
                    var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render((Visual)window.Content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var file = File.Create(output)) encoder.Save(file);
                    File.WriteAllText(output + ".json", JsonSerializer.Serialize(new { widths, captureTimesSeconds, processPriority = Process.GetCurrentProcess().PriorityClass.ToString(), stable = widths.All(w => w == widths[0]), contentWidth = ((FrameworkElement)window.Content).ActualWidth, dpi = VisualTreeHelper.GetDpi(window).DpiScaleX }));
                    capture.Stop(); window.Close();
                };
                window.Loaded += (_, _) => { captureClock.Restart(); capture.Start(); };
            }
            app.Run(window);
        }
        catch (Exception e) { Log(e); if (!args.Any(a => a.Contains("worker"))) System.Windows.MessageBox.Show(e.Message, "RigPulse", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    public static void Log(Exception e) { try { Directory.CreateDirectory(Settings.DirectoryPath); File.AppendAllText(Path.Combine(Settings.DirectoryPath, "rigpulse.log"), $"{DateTimeOffset.UtcNow:u} {e}\n"); } catch { } }
}
