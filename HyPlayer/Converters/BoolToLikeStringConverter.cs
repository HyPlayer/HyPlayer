using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class BoolToLikeStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return "已收藏";
            else return "收藏";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
