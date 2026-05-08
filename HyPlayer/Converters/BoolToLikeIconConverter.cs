using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public class BoolToLikeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return "\uE10B";
            else return "\uE0B4";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
