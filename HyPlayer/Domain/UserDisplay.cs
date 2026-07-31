using System;

namespace HyPlayer.Domain;

public record class UserDisplay(CommentUserInfo User, bool NoImage)
{
    public Uri AvatarUri => NoImage
        ? new Uri("ms-appx:///Assets/icon.png")
        : new Uri(User.AvatarUrl ?? string.Empty, UriKind.RelativeOrAbsolute);
}