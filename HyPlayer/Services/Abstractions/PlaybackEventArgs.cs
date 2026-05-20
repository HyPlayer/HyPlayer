using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.Services.Playback;
using System;

namespace HyPlayer.Services.Abstractions;

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

public sealed class LyricLoadedEventArgs(HyLyricInfo info) : EventArgs
{
    public HyLyricInfo Info { get; } = info;
}

public sealed class LyricIndexChangedEventArgs(int index) : EventArgs
{
    public int Index { get; } = index;
}

public sealed class PlaybackTrackChangedEventArgs(HyPlayItem item) : EventArgs
{
    public HyPlayItem Item { get; } = item;
}

public sealed class PlaybackThemeChangedEventArgs(PlaybackThemeSnapshot theme) : EventArgs
{
    public PlaybackThemeSnapshot Theme { get; } = theme;
}

public sealed class PlaybackSurfaceModeChangedEventArgs(PlaybackSurfaceMode mode) : EventArgs
{
    public PlaybackSurfaceMode Mode { get; } = mode;
}
