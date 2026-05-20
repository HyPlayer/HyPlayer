using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters
{
    public partial class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is not string strVal || string.IsNullOrEmpty(strVal) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}
