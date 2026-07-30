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
            var prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
            if (prepared is not null)
            {
                _prepared = prepared;
                try
                {
                    await prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
                    await prepared.Ticket.SetVolumeAsync(0, ct).ConfigureAwait(false);
                }
                catch (Exception effectException)
                {
                    try
                    {
                        await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        throw new AggregateException(effectException, cleanupException);
                    }

                    throw;
                }
            }
        }

        if (_prepared is null || remaining > context.CrossFadeDuration)
            return;

        var incoming = _prepared;
        PreparedPlaybackPromotion? promotion;
        try
        {
            await incoming.Ticket.PlayAsync(ct).ConfigureAwait(false);
            promotion = await context.Host.PromoteAsync(incoming, ct).ConfigureAwait(false);
        }
        catch (Exception startException)
        {
            try
            {
                await ReleasePreparedAsync(incoming).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(startException, cleanupException);
            }

            throw;
        }

        if (promotion is null)
        {
            await ReleasePreparedAsync(incoming).ConfigureAwait(false);
            return;
        }

        _prepared = null;
        _promotion = promotion;
        _fadeStartedAt = Stopwatch.GetTimestamp();
        await ApplyFadeAsync(context.CrossFadeDuration, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_promotion is not null)
        {
            await CompleteFadeAsync().ConfigureAwait(false);
            return;
        }

        var prepared = _prepared;
        if (prepared is not null)
        {
            PreparedPlaybackPromotion? promotion;
            try
            {
                await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
                promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
            }
            catch (Exception startException)
            {
                try
                {
                    await ReleasePreparedAsync(prepared).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(startException, cleanupException);
                }

                throw;
            }

            if (promotion is not null)
            {
                _prepared = null;
                _promotion = promotion;
                await CompleteFadeAsync().ConfigureAwait(false);
                return;
            }

            await ReleasePreparedAsync(prepared).ConfigureAwait(false);
        }

        await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        await CompleteFadeAsync().ConfigureAwait(false);

        if (_prepared is { } prepared)
            await ReleasePreparedAsync(prepared).ConfigureAwait(false);
    }

    private async Task ApplyFadeAsync(TimeSpan duration, CancellationToken ct)
    {
        if (_promotion is not { } promotion)
            return;

        var totalSeconds = Math.Max(duration.TotalSeconds, 0.001);
        var elapsedSeconds = (Stopwatch.GetTimestamp() - _fadeStartedAt) / (double)Stopwatch.Frequency;
        var progress = Math.Clamp(elapsedSeconds / totalSeconds, 0d, 1d);
        var angle = progress * Math.PI / 2d;

        try
        {
            await promotion.Incoming
                .SetVolumeAsync(promotion.Incoming.TargetVolume * Math.Sin(angle), ct)
                .ConfigureAwait(false);
            if (promotion.Outgoing is not null)
            {
                await promotion.Outgoing
                    .SetVolumeAsync(promotion.Outgoing.TargetVolume * Math.Cos(angle), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception effectException)
        {
            try
            {
                await CompleteFadeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(effectException, cleanupException);
            }

            throw;
        }

        if (progress >= 1d)
            await CompleteFadeAsync().ConfigureAwait(false);
    }

    private async Task CompleteFadeAsync()
    {
        var promotion = _promotion;
        if (promotion is null)
            return;

        Exception? normalizationException = null;
        try
        {
            await promotion.Incoming
                .SetVolumeAsync(promotion.Incoming.TargetVolume, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            normalizationException = ex;
        }

        try
        {
            await promotion.SettleOutgoingAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (normalizationException is not null)
        {
            throw new AggregateException(normalizationException, ex);
        }
        finally
        {
            if (promotion.Outgoing is null && ReferenceEquals(_promotion, promotion))
            {
                _promotion = null;
                _fadeStartedAt = 0;
            }
        }

        if (normalizationException is not null)
            throw normalizationException;
    }

    private async Task ReleasePreparedAsync(TransitionPreparedTrack prepared)
    {
        await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        if (ReferenceEquals(_prepared, prepared))
            _prepared = null;
    }
}
