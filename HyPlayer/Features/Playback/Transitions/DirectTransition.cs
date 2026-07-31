using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class DirectTransition : ITrackTransition
{
    public string Id => "dir";

    public Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        return context.Host.AdvanceDirectAsync(context, ct);
    }

    public Task CancelAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}