using System;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class ImageUrlToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return new BitmapImage(new Uri(value.ToString() + "?param=70y70"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
