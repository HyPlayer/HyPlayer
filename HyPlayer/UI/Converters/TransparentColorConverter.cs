using System;
using Windows.UI;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters
{
    public partial class TransparentColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            Color convert = (Color)value;
            return Color.FromArgb(0, convert.R, convert.G, convert.B);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
