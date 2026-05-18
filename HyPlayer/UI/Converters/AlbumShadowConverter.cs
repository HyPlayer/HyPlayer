using System;
using Windows.UI.Xaml.Data;

using CommunityToolkit.Mvvm.DependencyInjection;
namespace HyPlayer.Classes
{
    public partial class AlbumShadowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return Ioc.Default.GetRequiredService<Setting>().albumRound || Ioc.Default.GetRequiredService<Setting>().expandAlbumBreath
                ? 0
                : (double)Ioc.Default.GetRequiredService<Setting>().expandedCoverShadowDepth / 10;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
