using System;

namespace HyPlayer.Domain;

public record class UserDisplay(CommentUserInfo User, bool NoImage)
{
    public Uri? AvatarUri
    {
        get
        {
            if (NoImage)
                return new Uri("ms-appx:///Assets/icon.png");

            return Uri.TryCreate(User.AvatarUrl, UriKind.Absolute, out var avatarUri)
                ? avatarUri
                : null;
        }
    }
}
