using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

public interface ITrackTransitionHost
{
    Task<TransitionPreparedTrack?> PrepareNextAsync(TrackTransitionContext context, CancellationToken ct);
    Task<PreparedPlaybackPromotion?> PromoteAsync(TransitionPreparedTrack prepared, CancellationToken ct);
    Task AdvanceDirectAsync(TrackTransitionContext context, CancellationToken ct);
    Task<bool> RampVolumeAsync(IPlaybackSource source, double targetVolume, TimeSpan duration);
    double GetVolume(IPlaybackSource source);
    Task<SingleSongBase?> PeekNextAsync(TrackTransitionContext context, CancellationToken ct);
    void SetTransitioning(bool transitioning);
    Task<bool> TryRunExclusiveAsync(Func<Task> work);
}
