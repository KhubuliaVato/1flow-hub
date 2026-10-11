using System.Windows.Controls;

namespace OneFlowHub.Views;

public partial class DetailsDrawer : UserControl
{
    public DetailsDrawer() => InitializeComponent();

    private void OnClose(object sender, System.Windows.RoutedEventArgs e)
    {
        if (System.Windows.Window.GetWindow(this)?.DataContext is OneFlowHub.ViewModels.MainVM vm) vm.Details = null;
    }
}
