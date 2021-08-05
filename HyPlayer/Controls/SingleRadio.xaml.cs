using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.Pages;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls
{
    public sealed partial class SingleRadio : UserControl
    {
        private readonly NCRadio Radio;

        public SingleRadio(NCRadio radio)
        {
            InitializeComponent();
            Radio = radio;
            TextBlockRadioName.Text = radio.name;
            TextBlockDJName.Text = radio.DJ.name;
            TextBlockLastProgram.Text = "最后一个节目: " + radio.lastProgramName;
            ImageRect.Source =
                new BitmapImage(new Uri(radio.cover + "?param=" + StaticSource.PICSIZE_SINGLENCRADIO_COVER));
        }

        private void Grid1_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlAccentAcrylicElementAccentMediumHighBrush"] as Brush;
            Common.NavigatePage(typeof(RadioPage), Radio);
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Grid1.Background = Application.Current.Resources["SystemControlAltLowAcrylicElementBrush"] as Brush;
            Grid1.BorderBrush =
                Application.Current.Resources["SystemControlBackgroundListMediumRevealBorderBrush"] as Brush;
        }

        private void Grid1_OnPointerExited(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        private void Grid1_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => SetUnfocusedState();

        private void SetUnfocusedState()
        {
            Grid1.Background = null;
            Grid1.BorderBrush = new SolidColorBrush();
        }

        private void Grid1_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            Grid1.Background =
                Application.Current.Resources["SystemControlChromeMediumAcrylicElementMediumBrush"] as Brush;
        }
    }
}