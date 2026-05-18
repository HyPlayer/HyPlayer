using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class EnumToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Enum enumValue) return System.Convert.ToInt32(enumValue);
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}
