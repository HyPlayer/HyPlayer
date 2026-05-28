using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using System;

namespace HyPlayer.Domain
{
    public record class UserDisplay(CommentUserInfo User)
    {
        public Uri AvatarUri => Ioc.Default.GetRequiredService<Setting>().noImage ? new("ms-appx:///Assets/icon.png") : new(User.AvatarUrl ?? string.Empty, UriKind.RelativeOrAbsolute);
    }
}
