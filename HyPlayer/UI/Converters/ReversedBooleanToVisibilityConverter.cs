using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters;

public partial class ReversedBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is true) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}