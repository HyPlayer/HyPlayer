using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class ScrollingTextBlock : UserControl
    {
        public string Text
        {
            set
            {
                Tb.Text = value;
            }
        }


        public ScrollingTextBlock()
        {
            this.InitializeComponent();
        }

        public double Horizontalofset
        {
            get { return (double)GetValue(HorizontalofsetProperty); }
            set { SetValue(HorizontalofsetProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Horizontalofset.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty HorizontalofsetProperty =
            DependencyProperty.Register("Horizontalofset", typeof(double), typeof(ScrollingTextBlock), new PropertyMetadata(0, PropertyChangedCallback));

        public static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var distance = (d as ScrollingTextBlock).scrolviewer.ScrollableWidth;
            if (!(e.NewValue is 0) || distance > (double)e.NewValue)
            {
                var ret = (d as ScrollingTextBlock).scrolviewer.ChangeView((double)e.NewValue, (d as ScrollingTextBlock).scrolviewer.VerticalOffset, (d as ScrollingTextBlock).scrolviewer.ZoomFactor);
            }

        }
    }
}
