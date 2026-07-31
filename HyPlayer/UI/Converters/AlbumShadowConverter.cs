using System;
using Windows.UI.Xaml.Data;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;

namespace HyPlayer.UI.Converters;

public partial class AlbumShadowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var settings = Ioc.Default.GetRequiredService<UISettings>();
        return settings.AlbumRound || settings.ExpandAlbumBreath
            ? 0
            : (double)settings.ExpandedCoverShadowDepth / 10;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
