using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class GaplessTransition : ITrackTransition
{
    private static readonly TimeSpan PreloadWindow = TimeSpan.FromSeconds(30);
    private TransitionPreparedTrack? _prepared;
    private PreparedPlaybackPromotion? _promotion;

    public string Id => "gap";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        if (_prepared is not null
            || !context.CanPreload
            || context.Duration < PreloadWindow
            || context.Duration - context.Position > PreloadWindow)
            return;

        var prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
        if (prepared is null)
            return;

        _prepared = prepared;
        try
        {
            await prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
        }
        catch
        {
            await ReleasePreparedAsync(prepared).ConfigureAwait(false);
            throw;
        }
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        var prepared = _prepared;
        if (prepared is not null)
        {
            try
            {
                await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
                return;
            }

            PreparedPlaybackPromotion? promotion;
            try
            {
                promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
                return;
            }

            if (promotion is not null)
            {
                _prepared = null;
                _promotion = promotion;
                await SettlePromotionAsync().ConfigureAwait(false);
                return;
            }

            await ReleasePreparedAsync(prepared).ConfigureAwait(false);
        }

        await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        await SettlePromotionAsync().ConfigureAwait(false);

        if (_prepared is { } prepared)
            await ReleasePreparedAsync(prepared).ConfigureAwait(false);
    }

    private async Task ReleasePreparedAsync(TransitionPreparedTrack prepared)
    {
        await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        if (ReferenceEquals(_prepared, prepared))
            _prepared = null;
    }

    private async Task SettlePromotionAsync()
    {
        var promotion = _promotion;
        if (promotion is null)
            return;

        try
        {
            await promotion.SettleOutgoingAsync().ConfigureAwait(false);
        }
        finally
        {
            if (promotion.Outgoing is null && ReferenceEquals(_promotion, promotion))
                _promotion = null;
        }
    }
}
