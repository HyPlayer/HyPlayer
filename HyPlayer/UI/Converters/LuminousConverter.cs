using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters
{
    public partial class LuminousConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is 6) return true;
            else return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
