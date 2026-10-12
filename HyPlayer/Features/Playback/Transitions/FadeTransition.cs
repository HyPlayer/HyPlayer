using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class FadeTransition : ITrackTransition
{
    private static readonly TimeSpan _preloadLead = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _minimumRemaining = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _fadeOutTail = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _restoreTime = TimeSpan.FromMilliseconds(200);
    private readonly NextTrackPreloader _preloader = new();
    private ITrackTransitionHost? _fadingHost;
    private IPlaybackSource? _fadingSource;
    private double _fadingVolume;
    private PreparedPlaybackPromotion? _promotion;

    public string Id => "fde";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        var fadeOutTime = context.CrossFadeDuration;
        await _preloader.PrepareAsync(context, fadeOutTime + _preloadLead, ct).ConfigureAwait(false);

        if (_fadingSource is not null && !ReferenceEquals(_fadingSource, context.Source))
            ClearFading();

        if (_fadingSource is not null || !context.CanPreload)
            return;

        var remaining = context.Duration - context.Position;
        if (remaining > fadeOutTime || remaining < _minimumRemaining)
            return;

        _fadingHost = context.Host;
        _fadingSource = context.Source;
        _fadingVolume = context.Host.GetVolume(context.Source);
        var rate = Math.Max(context.PlaybackRate, 0.1);
        _ = context.Host.RampVolumeAsync(context.Source, 0, (remaining - _fadeOutTail) / rate);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await SettlePromotionAsync().ConfigureAwait(false);
            return;
        }

        ClearFading();
        var prepared = _preloader.Take();
        _preloader.CancelLoading();
        if (prepared is not null)
        {
            PreparedPlaybackPromotion? promotion;
            try
            {
                await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
                promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            catch
            {
                promotion = null;
            }

            if (promotion is not null)
            {
                _promotion = promotion;
                await FadeInAsync(context, prepared).ConfigureAwait(false);
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
        RestoreFading();
        if (target < context.Duration - context.CrossFadeDuration - _preloadLead)
            await _preloader.DiscardAsync().ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        await SettlePromotionAsync().ConfigureAwait(false);
        RestoreFading();
        await _preloader.DiscardAsync().ConfigureAwait(false);
    }

    private void RestoreFading()
    {
        if (_fadingSource is not { } source || _fadingHost is not { } host)
            return;

        var volume = _fadingVolume;
        ClearFading();
        _ = host.RampVolumeAsync(source, volume, _restoreTime);
    }

    private static async Task FadeInAsync(TrackTransitionContext context, TransitionPreparedTrack prepared)
    {
        var target = prepared.Ticket.TargetVolume;
        if (prepared.IncomingSource is { } incoming)
        {
            var fadeInTime = TimeSpan.FromSeconds(Math.Clamp(context.CrossFadeDuration.TotalSeconds / 2, 0.5, 4));
            _ = context.Host.RampVolumeAsync(incoming, target, fadeInTime);
            return;
        }

        await prepared.Ticket.SetVolumeAsync(target, CancellationToken.None).ConfigureAwait(false);
    }

    private void ClearFading()
    {
        _fadingHost = null;
        _fadingSource = null;
        _fadingVolume = 0;
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
