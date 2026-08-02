using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

public interface ITrackTransition
{
    string Id { get; }
    Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct);
    Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct);
    Task CancelAsync(CancellationToken ct);
}
