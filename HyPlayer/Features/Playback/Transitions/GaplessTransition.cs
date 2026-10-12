using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class GaplessTransition : ITrackTransition
{
    private static readonly TimeSpan _preloadWindow = TimeSpan.FromSeconds(30);
    private readonly NextTrackPreloader _preloader = new(false);
    private PreparedPlaybackPromotion? _promotion;

    public string Id => "gap";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        await _preloader.PrepareAsync(context, _preloadWindow, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        var prepared = _preloader.Take();
        _preloader.CancelLoading();
        if (prepared is not null)
        {
            try
            {
                await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
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
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
                await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
                return;
            }

            if (promotion is not null)
            {
                _promotion = promotion;
                await SettlePromotionAsync().ConfigureAwait(false);
                return;
            }

            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        }

        await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
    }

    public async Task OnSeekAsync(TrackTransitionContext context, TimeSpan target, CancellationToken ct)
    {
        await SettlePromotionAsync().ConfigureAwait(false);
        if (target < context.Duration - _preloadWindow)
            await _preloader.DiscardAsync().ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        await SettlePromotionAsync().ConfigureAwait(false);
        await _preloader.DiscardAsync().ConfigureAwait(false);
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