using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using OneFlowHub.Core;
using OneFlowHub.ViewModels;

namespace OneFlowHub;

public partial class MainWindow : Window
{
    private readonly MainVM _vm;

    public MainWindow(MainVM vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

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
        if (e.PropertyName == nameof(MainVM.HasDetails)) AnimateDetails(_vm.HasDetails);
        else if (e.PropertyName == nameof(MainVM.ToastOn)) AnimateToast(_vm.ToastOn);
    }

    // ── details overlay
    private void AnimateDetails(bool open)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (open)
        {
            DetailsHost.Opacity = 0;
            DetailsHost.Visibility = Visibility.Visible;
            DetailsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            DShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            DScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            DScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        }
        else
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (_, _) => { if (!_vm.HasDetails) DetailsHost.Visibility = Visibility.Collapsed; };
            DetailsHost.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void AnimateToast(bool on)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(260)));
        ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(on ? 0 : 16, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
    }

    private void OnOverlayDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _vm.Details = null;
    private void OnCardDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;
    private void OnDetailsClose(object sender, RoutedEventArgs e) => _vm.Details = null;

    // ── caption buttons
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
