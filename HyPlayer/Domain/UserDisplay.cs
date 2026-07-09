using System;

namespace HyPlayer.Domain
{
    public record class UserDisplay(CommentUserInfo User, bool NoImage)
    {
        public Uri AvatarUri => NoImage
            ? new("ms-appx:///Assets/icon.png")
            : new(User.AvatarUrl ?? string.Empty, UriKind.RelativeOrAbsolute);
    }
}
