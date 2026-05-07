using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using WinRT;

using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
namespace HyPlayer.Classes
{
    public partial class BooleanToGridPlayBarBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is false && Ioc.Default.GetRequiredService<Setting>().acrylicBackgroundStatus is true && Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded is false) return Application.Current.Resources["GridPlayBarBackgroundAcrylic"]?.As<Brush>();
            if (value is false && Ioc.Default.GetRequiredService<Setting>().acrylicBackgroundStatus is false && Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded is false) return Application.Current.Resources["SystemControlAcrylicElementMediumHighBrush"]?.As<Brush>();
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
