using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;

namespace HyPlayer.Features.Playback.Transitions;

public interface ITrackTransitionHost
{
    Task<TransitionPreparedTrack?> PrepareNextAsync(TrackTransitionContext context, CancellationToken ct);
    Task<PreparedPlaybackPromotion?> PromoteAsync(TransitionPreparedTrack prepared, CancellationToken ct);
    Task AdvanceDirectAsync(TrackTransitionContext context, CancellationToken ct);
}
