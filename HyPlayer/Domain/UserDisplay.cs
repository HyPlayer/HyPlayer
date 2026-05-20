using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using System;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Domain
{
    public record class UserDisplay(NCUser User)
    {
        public Uri AvatarUri => Ioc.Default.GetRequiredService<Setting>().noImage ? new("ms-appx:///Assets/icon.png") : new(User.Avatar, UriKind.RelativeOrAbsolute);
    }
}
