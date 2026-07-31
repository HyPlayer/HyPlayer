using System;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

public sealed class PlaylistChangedEventArgs(bool isShuffleTrigger = false) : EventArgs
{
    public bool IsShuffleTrigger { get; } = isShuffleTrigger;
}

public sealed class SeekRequestedEventArgs(TimeSpan position) : EventArgs
{
    public TimeSpan Position { get; } = position;
}

public sealed class SongLikeStatusChangedEventArgs(bool isLiked) : EventArgs
{
    public bool IsLiked { get; } = isLiked;
}

public sealed class PlaybackTrackChangedEventArgs(SingleSongBase item) : EventArgs
{
    public SingleSongBase Item { get; } = item;
}

public sealed class PlaybackThemeChangedEventArgs(PlaybackThemeSnapshot theme) : EventArgs
{
    public PlaybackThemeSnapshot Theme { get; } = theme;
}

public sealed class PlaybackSurfaceModeChangedEventArgs(PlaybackSurfaceMode mode) : EventArgs
{
    public PlaybackSurfaceMode Mode { get; } = mode;
}