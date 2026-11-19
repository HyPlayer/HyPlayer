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

            if (!Uri.TryCreate(User.AvatarUrl, UriKind.Absolute, out var avatarUri))
                return null;

            return avatarUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                   && string.IsNullOrEmpty(avatarUri.Query)
                ? new Uri($"{avatarUri}?param={StaticSource.PicSizeCommentUserAvatar}")
                : avatarUri;
        }
    }
}
