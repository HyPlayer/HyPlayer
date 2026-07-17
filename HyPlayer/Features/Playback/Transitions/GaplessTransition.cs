using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class GaplessTransition : ITrackTransition
{
    private static readonly TimeSpan PreloadWindow = TimeSpan.FromSeconds(30);
    private TransitionPreparedTrack? _prepared;

    public string Id => "gap";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (context.HasActiveAbLoop)
        {
            await CancelAsync(ct).ConfigureAwait(false);
            return;
        }

        if (_prepared is not null
            || !context.CanPreload
            || context.Duration < PreloadWindow
            || context.Duration - context.Position > PreloadWindow)
            return;

        _prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
        if (_prepared is not null)
            await _prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        var prepared = _prepared;
        _prepared = null;
        if (prepared is not null)
        {
            var promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
            if (promotion is not null)
            {
                await promotion.Incoming.PlayAsync(ct).ConfigureAwait(false);
                if (promotion.Outgoing is not null)
                    await promotion.Outgoing.DisposeAsync().ConfigureAwait(false);
                return;
            }

            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        }

        await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        var prepared = _prepared;
        _prepared = null;
        if (prepared is not null)
            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
    }
}
