using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class DirectTransition : ITrackTransition
{
    public string Id => "dir";

    public Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct) =>
        Task.CompletedTask;

    public Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct) =>
        context.Host.AdvanceDirectAsync(context, ct);

    public Task CancelAsync(CancellationToken ct) => Task.CompletedTask;
}
