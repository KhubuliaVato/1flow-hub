using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OneFlowHub.Core;
using OneFlowHub.ViewModels;

namespace OneFlowHub;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private Tray? _tray;
    private MainWindow? _window;
    private MainVM? _vm;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Paths.EnsureAll();

        // one instance only — a second launch just brings the first one forward
        _single = new Mutex(true, "1FlowHub.SingleInstance", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "1FlowHub.Show");
        bool snapshotRun = e.Args.Any(a => a.StartsWith("--snapshot="));
        if (!first && !snapshotRun)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) => { Log.Error(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error(args.Exception); args.SetObserved(); };

        var settings = AppSettings.Load();
        // dev switch: --catalog=<local file or url> (not saved)
        var catArg = e.Args.FirstOrDefault(a => a.StartsWith("--catalog="));
        if (catArg != null) settings.CatalogUrl = catArg[10..];
        _vm = new MainVM(settings);
        _window = new MainWindow(_vm);

        _tray = new Tray(ShowWindow, () => _ = _vm.CheckHubAsync(force: true), ExitApp);

        var th = new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.Invoke(ShowWindow);
        }) { IsBackground = true };
        th.Start();

        string? snapshot = e.Args.FirstOrDefault(a => a.StartsWith("--snapshot="))?[11..];
        if (snapshot != null)
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Left = -6000; _window.Top = 0;
            _window.ShowInTaskbar = false;
        }

        bool startHidden = e.Args.Contains("--tray") && settings.CloseToTray;
        if (!startHidden || snapshot != null) _window.Show();

        await _vm.LoadAsync();

        // developer/test switches: --page=1  --details=<app id>
        foreach (var a in e.Args)
        {
            if (a.StartsWith("--page=") && int.TryParse(a[7..], out var pg)) _vm.Page = pg;
            if (a.StartsWith("--details="))
            {
                var app = _vm.Apps.FirstOrDefault(x => x.Id == a[10..]);
                if (app != null) _vm.OpenDetails(app);
            }
        }

        if (snapshot != null)
        {
            await Task.Delay(3500);   // let icons load and entrance animations finish
            SaveSnapshot(_window, snapshot);
            Shutdown();
        }
    }

    /// <summary>Dev helper: renders the window's visual tree to a PNG without touching the screen.</summary>
    private static void SaveSnapshot(Window w, string path)
    {
        try
        {
            var root = (FrameworkElement)w.Content;
            root.UpdateLayout();
            int pw = (int)root.ActualWidth, ph = (int)root.ActualHeight;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x08, 0x0A, 0x1C)), null, new Rect(0, 0, pw, ph));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, pw, ph));
            }
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    private void ShowWindow()
    {
        if (_window == null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true; _window.Topmost = false;
    }

    private void ExitApp()
    {
        if (_window != null) _window.ReallyExit = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try { _single?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
