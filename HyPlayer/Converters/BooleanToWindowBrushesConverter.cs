using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using WinRT;

using CommunityToolkit.Mvvm.DependencyInjection;
namespace HyPlayer.Classes
{
    public partial class BooleanToWindowBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true && Ioc.Default.GetRequiredService<Setting>().CustomAcrylic is true)
            {
                var Brush = new Windows.UI.Xaml.Media.AcrylicBrush()
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
                    TintOpacity = Ioc.Default.GetRequiredService<Setting>().CustomTintOpacity,
                    TintLuminosityOpacity = Ioc.Default.GetRequiredService<Setting>().CustomTintLuminosityOpacity,
                    FallbackColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
                };
                return Brush;
            }
            if (value is true && Ioc.Default.GetRequiredService<Setting>().CustomAcrylic is false)
                return Application.Current.Resources["NormalWindowBackgroundAcrylic"]?.As<Brush>();
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
