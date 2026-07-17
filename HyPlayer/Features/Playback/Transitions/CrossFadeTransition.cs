using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class CrossFadeTransition : ITrackTransition
{
    private static readonly TimeSpan PreloadWindow = TimeSpan.FromSeconds(30);
    private TransitionPreparedTrack? _prepared;
    private PreparedPlaybackPromotion? _promotion;
    private long _fadeStartedAt;

    public string Id => "xfd";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (context.HasActiveAbLoop)
        {
            await CancelAsync(ct).ConfigureAwait(false);
            return;
        }

        if (_promotion is not null)
        {
            await ApplyFadeAsync(context.CrossFadeDuration, ct).ConfigureAwait(false);
            return;
        }

        if (!context.CanPreload || context.Duration < PreloadWindow)
            return;

        var remaining = context.Duration - context.Position;
        if (_prepared is null && remaining <= PreloadWindow)
        {
            _prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
            if (_prepared is not null)
            {
                await _prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
                await _prepared.Ticket.SetVolumeAsync(0, ct).ConfigureAwait(false);
            }
        }

        if (_prepared is null || remaining > context.CrossFadeDuration)
            return;

        var prepared = _prepared;
        await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
        _promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
        if (_promotion is null)
        {
            _prepared = null;
            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _prepared = null;
        _fadeStartedAt = Stopwatch.GetTimestamp();
        await ApplyFadeAsync(context.CrossFadeDuration, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await CompleteFadeAsync(ct).ConfigureAwait(false);
            return;
        }

        var prepared = _prepared;
        _prepared = null;
        if (prepared is not null)
        {
            await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
            var promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
            if (promotion is not null)
            {
                await promotion.Incoming.SetVolumeAsync(promotion.Incoming.TargetVolume, ct).ConfigureAwait(false);
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

        if (_promotion is not null)
            await CompleteFadeAsync(ct).ConfigureAwait(false);
    }

    private async Task ApplyFadeAsync(TimeSpan duration, CancellationToken ct)
    {
        if (_promotion is not { } promotion)
            return;

        var totalSeconds = Math.Max(duration.TotalSeconds, 0.001);
        var elapsedSeconds = (Stopwatch.GetTimestamp() - _fadeStartedAt) / (double)Stopwatch.Frequency;
        var progress = Math.Clamp(elapsedSeconds / totalSeconds, 0d, 1d);
        var angle = progress * Math.PI / 2d;

        await promotion.Incoming
            .SetVolumeAsync(promotion.Incoming.TargetVolume * Math.Sin(angle), ct)
            .ConfigureAwait(false);
        if (promotion.Outgoing is not null)
        {
            await promotion.Outgoing
                .SetVolumeAsync(promotion.Outgoing.TargetVolume * Math.Cos(angle), ct)
                .ConfigureAwait(false);
        }

        if (progress >= 1d)
            await CompleteFadeAsync(ct).ConfigureAwait(false);
    }

    private async Task CompleteFadeAsync(CancellationToken ct)
    {
        var promotion = _promotion;
        _promotion = null;
        _fadeStartedAt = 0;
        if (promotion is null)
            return;

        await promotion.Incoming
            .SetVolumeAsync(promotion.Incoming.TargetVolume, ct)
            .ConfigureAwait(false);
        if (promotion.Outgoing is not null)
            await promotion.Outgoing.DisposeAsync().ConfigureAwait(false);
    }
}
