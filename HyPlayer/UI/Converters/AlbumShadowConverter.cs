using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters
{
    public partial class AlbumShadowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var setting = Ioc.Default.GetRequiredService<Setting>();
            return setting.albumRound || setting.expandAlbumBreath
                ? 0
                : (double)setting.expandedCoverShadowDepth / 10;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
