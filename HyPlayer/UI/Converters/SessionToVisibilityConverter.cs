using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using LiteFM.Abstractions;

namespace HyPlayer.UI.Converters;

public abstract class SessionToVisibilityConverterBase : IValueConverter
{
    protected virtual bool Negate => false;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasLogined = value is LastFMSession session && session.HasLogined;
        return hasLogined ^ Negate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public partial class SessionToVisibilityConverter : SessionToVisibilityConverterBase
{
}