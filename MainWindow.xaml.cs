using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OneFlowHub.Core;
using OneFlowHub.ViewModels;

namespace OneFlowHub;

public partial class MainWindow : Window
{
    private readonly MainVM _vm;
    private readonly FrameworkElement[] _pages;
    private int _shownPage = 0;

    public MainWindow(MainVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _pages = new FrameworkElement[] { HomePage, LibraryPage, UpdatesPage, SettingsPage };
        foreach (var p in _pages)
        {
            p.RenderTransform = new TranslateTransform();
            p.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        SourceInitialized += (_, _) => Dwm.Apply(this);
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Closing += OnWindowClosing;
        vm.PropertyChanged += OnVmChanged;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && _vm.HasDetails) _vm.Details = null;
        };
    }

    public bool ReallyExit { get; set; }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!ReallyExit && _vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainVM.Page): ShowPage(_vm.Page); break;
            case nameof(MainVM.HasDetails): AnimateDrawer(_vm.HasDetails); break;
            case nameof(MainVM.ToastOn): AnimateToast(_vm.ToastOn); break;
        }
    }

    // ── page switch: fade + slight rise
    private void ShowPage(int index)
    {
        if (index == _shownPage || index < 0 || index >= _pages.Length) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < _pages.Length; i++)
            if (i != index) _pages[i].Visibility = Visibility.Collapsed;

        var page = _pages[index];
        page.Visibility = Visibility.Visible;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        _shownPage = index;
    }

    // ── details drawer: scrim fades, panel slides from the right
    private void AnimateDrawer(bool open)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (open)
        {
            DrawerHost.Opacity = 0;
            DrawerHost.Visibility = Visibility.Visible;
            DrawerHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            DrawerShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(DrawerPanel.Width, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        }
        else
        {
            DrawerShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, DrawerPanel.Width, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _) => { if (!_vm.HasDetails) DrawerHost.Visibility = Visibility.Collapsed; };
            DrawerHost.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void AnimateToast(bool on)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(260)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(on ? 0 : 16, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
    }

    private void OnScrimDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _vm.Details = null;
    private void OnPanelDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

    // ── caption buttons
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
