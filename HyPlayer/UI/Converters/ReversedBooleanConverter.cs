using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters
{
    public partial class ReversedBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return false;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
