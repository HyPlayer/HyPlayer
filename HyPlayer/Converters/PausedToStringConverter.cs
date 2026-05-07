using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class PausedToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (parameter is true) return "重试";
            return value is true ? "继续" : "暂停";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
