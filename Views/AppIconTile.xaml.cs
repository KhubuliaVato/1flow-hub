using System.Windows;
using System.Windows.Controls;

namespace OneFlowHub.Views;

public partial class AppIconTile : UserControl
{
    public AppIconTile()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Apply();
        Loaded += (_, _) => Apply();
    }

    private void Apply()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(w) || w <= 0) return;
        Frame.CornerRadius = new CornerRadius(w * 0.24);
        Glyph.FontSize = w * 0.42;
        Img.Margin = new Thickness(0);
    }
}
