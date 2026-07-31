using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters;

public partial class ThumbConverter : DependencyObject, IValueConverter
{
    public static readonly DependencyProperty SecondValueProperty =
        DependencyProperty.Register("SecondValue", typeof(double), typeof(ThumbConverter),
            new PropertyMetadata(0d));

    public double SecondValue
    {
        get => (double)GetValue(SecondValueProperty);
        set => SetValue(SecondValueProperty, value);
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return TimeSpan.FromMilliseconds(double.Parse(value.ToString())).ToString(@"hh\:mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}