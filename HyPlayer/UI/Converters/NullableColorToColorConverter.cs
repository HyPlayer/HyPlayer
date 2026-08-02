using System;
using Windows.UI;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters;

public partial class NullableColorToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Color c) return c;

        return Colors.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value;
    }
}