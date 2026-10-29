namespace HyPlayer.Application.Sharing;

public enum ShareRequestKind
{
    None,
    Song,
    Playlist
}

/// <summary>
///     Identifies which share entry point initiated the current system share request.
/// </summary>
public sealed class ShareRequestState
{
    public ShareRequestKind RequestedKind { get; private set; }

    public void Request(ShareRequestKind kind)
    {
        RequestedKind = kind;
    }
}
