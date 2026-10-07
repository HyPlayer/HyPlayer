using System;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.UI.Converters;

public partial class ImageUrlToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new BitmapImage(new Uri(url + separator + "param=70y70"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
