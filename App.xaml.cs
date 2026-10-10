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
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) => { Log.Error(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error(args.Exception); args.SetObserved(); };

        var settings = AppSettings.Load();
        _vm = new MainVM(settings);
        _window = new MainWindow(_vm);

        _tray = new Tray(ShowWindow, () => _ = _vm.CheckHubAsync(force: true), ExitApp);

        var th = new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.Invoke(ShowWindow);
        }) { IsBackground = true };
        th.Start();

        bool startHidden = e.Args.Contains("--tray") && settings.CloseToTray;
        if (!startHidden) _window.Show();

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
