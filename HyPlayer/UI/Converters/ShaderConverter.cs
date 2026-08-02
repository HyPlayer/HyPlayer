using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using HyPlayer.Domain;

namespace HyPlayer.UI.Converters;

public partial class ShaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is BackgroundType.Isolation) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}