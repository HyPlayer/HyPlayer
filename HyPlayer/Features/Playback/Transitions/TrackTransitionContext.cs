using System;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class TrackTransitionContext
{
    public required ITrackTransitionHost Host { get; init; }
    public required IPlaybackSource Source { get; init; }
    public required long Generation { get; init; }
    public required TimeSpan Position { get; init; }
    public required TimeSpan Duration { get; init; }
    public required bool CanPreload { get; init; }
    public required double PlaybackRate { get; init; }
    public required TimeSpan CrossFadeDuration { get; init; }
}

public sealed class TransitionPreparedTrack
{
    public required SingleSongBase Song { get; init; }
    public required PreparedPlaybackTicket Ticket { get; init; }
    public required long Generation { get; init; }
    public required IPlaybackSource Source { get; init; }
    public required int CurrentIndex { get; init; }
    public required int QueueRevision { get; init; }
}
