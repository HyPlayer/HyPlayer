#region

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class ScrollingTextBlock : UserControl
{
    // Using a DependencyProperty as the backing store for Horizontalofset.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty HorizontalofsetProperty =
        DependencyProperty.Register("Horizontalofset", typeof(double), typeof(ScrollingTextBlock),
            new PropertyMetadata(0, PropertyChangedCallback));


    public ScrollingTextBlock()
    {
        InitializeComponent();
    }

    public string Text
    {
        set => Tb.Text = value;
    }

    public double Horizontalofset
    {
        get => (double)GetValue(HorizontalofsetProperty);
        set => SetValue(HorizontalofsetProperty, value);
    }

    public static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var distance = (d as ScrollingTextBlock).scrolviewer.ScrollableWidth;
        if (!(e.NewValue is 0) || distance > (double)e.NewValue)
        {
            var ret = (d as ScrollingTextBlock).scrolviewer.ChangeView((double)e.NewValue,
                (d as ScrollingTextBlock).scrolviewer.VerticalOffset,
                (d as ScrollingTextBlock).scrolviewer.ZoomFactor);
        }
    }
}