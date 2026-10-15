using System;

namespace HyPlayer.Features.Playback.Services;

public enum PlaybackSurfaceCommand
{
    TogglePlayback,
    MovePrevious,
    MoveNext,
    ChangePlayMode,
    ToggleLike,
    ShowQueue,
    OpenDesktopLyrics,
    AddToSongList,
    Download,
    OpenComments,
    Share
}

public sealed class PlaybackSurfaceCommandRequestedEventArgs(PlaybackSurfaceCommand command, object? anchor) : EventArgs
{
    public PlaybackSurfaceCommand Command { get; } = command;
    public object? Anchor { get; } = anchor;
}
