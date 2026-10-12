using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class CrossFadeTransition : ITrackTransition
{
    private static readonly TimeSpan _preloadLead = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _minimumRemaining = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan _restoreTime = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _catchUpTime = TimeSpan.FromMilliseconds(250);
    private readonly NextTrackPreloader _preloader = new();
    private ActiveFade? _fade;
    private PreparedPlaybackPromotion? _settling;

    public string Id => "xfd";

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_fade is { } fade)
        {
            if (fade.Promotion is null)
            {
                if (context.Position - fade.StartPosition >= fade.HalfSpan)
                    await CommitAsync(fade, ct).ConfigureAwait(false);
            }
            else if (fade.FadeOut.IsCompleted)
            {
                await CompleteAsync(fade).ConfigureAwait(false);
            }

            return;
        }

        if (_settling is not null)
        {
            await SettleAsync().ConfigureAwait(false);
            return;
        }

        await _preloader
            .PrepareAsync(context, context.CrossFadeDuration + _preloadLead, ct)
            .ConfigureAwait(false);
        if (_preloader.Prepared is not { IncomingSource: not null })
            return;

        var remaining = context.Duration - context.Position;
        if (remaining > context.CrossFadeDuration || remaining < _minimumRemaining)
            return;

        await BeginFadeAsync(context, remaining, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (_fade is { } fade)
        {
            if (fade.Promotion is not null)
            {
                await CompleteAsync(fade).ConfigureAwait(false);
                await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
                return;
            }

            if (await CommitAsync(fade, ct).ConfigureAwait(false))
            {
                _fade = null;
                _settling = fade.Promotion;
                _ = fade.Host.RampVolumeAsync(fade.IncomingSource, fade.Incoming.Ticket.TargetVolume, _catchUpTime);
                await SettleAsync().ConfigureAwait(false);
                return;
            }

            await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
            return;
        }

        if (_settling is not null)
        {
            await SettleAsync().ConfigureAwait(false);
            return;
        }

        var prepared = _preloader.Take();
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
                _settling = promotion;
                if (prepared.IncomingSource is { } incoming)
                    _ = context.Host.RampVolumeAsync(incoming, prepared.Ticket.TargetVolume, _catchUpTime);
                else
                    await NormalizeIncomingAsync(promotion).ConfigureAwait(false);
                await SettleAsync().ConfigureAwait(false);
                return;
            }

            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        }

        await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        if (_fade is { } fade)
        {
            if (fade.Promotion is null)
                await AbortAsync(fade).ConfigureAwait(false);
            else
                await CompleteAsync(fade).ConfigureAwait(false);
        }

        await SettleAsync().ConfigureAwait(false);
        await _preloader.DiscardAsync().ConfigureAwait(false);
    }

    private async Task BeginFadeAsync(TrackTransitionContext context, TimeSpan remaining, CancellationToken ct)
    {
        var prepared = _preloader.Take();
        if (prepared?.IncomingSource is not { } incomingSource)
            return;

        try
        {
            await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception startException)
        {
            try
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(startException, cleanupException);
            }

            throw;
        }

        var duration = remaining / Math.Max(context.PlaybackRate, 0.1);
        var outgoingVolume = context.Host.GetVolume(context.Source);
        _ = context.Host.RampVolumeAsync(incomingSource, prepared.Ticket.TargetVolume, duration);
        _fade = new ActiveFade
        {
            Host = context.Host,
            Incoming = prepared,
            IncomingSource = incomingSource,
            Outgoing = context.Source,
            OutgoingVolume = outgoingVolume,
            StartPosition = context.Position,
            HalfSpan = remaining / 2,
            FadeOut = context.Host.RampVolumeAsync(context.Source, 0, duration)
        };
    }

    private async Task<bool> CommitAsync(ActiveFade fade, CancellationToken ct)
    {
        PreparedPlaybackPromotion? promotion;
        try
        {
            promotion = await fade.Host.PromoteAsync(fade.Incoming, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await AbortAsync(fade).ConfigureAwait(false);
            throw;
        }
        catch
        {
            promotion = null;
        }

        if (promotion is null)
        {
            await AbortAsync(fade).ConfigureAwait(false);
            return false;
        }

        fade.Promotion = promotion;
        return true;
    }

    private async Task AbortAsync(ActiveFade fade)
    {
        if (ReferenceEquals(_fade, fade))
            _fade = null;

        _ = fade.Host.RampVolumeAsync(fade.Outgoing, fade.OutgoingVolume, _restoreTime);
        await fade.Incoming.Ticket.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CompleteAsync(ActiveFade fade)
    {
        if (ReferenceEquals(_fade, fade))
            _fade = null;

        if (fade.Promotion is not { } promotion)
            return;

        _settling = promotion;
        Exception? normalizationException = null;
        try
        {
            await NormalizeIncomingAsync(promotion).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            normalizationException = ex;
        }

        try
        {
            await SettleAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (normalizationException is not null)
        {
            throw new AggregateException(normalizationException, ex);
        }

        if (normalizationException is not null)
            throw normalizationException;
    }

    private static Task NormalizeIncomingAsync(PreparedPlaybackPromotion promotion)
    {
        return promotion.Incoming.SetVolumeAsync(promotion.Incoming.TargetVolume, CancellationToken.None);
    }

    private async Task SettleAsync()
    {
        var promotion = _settling;
        if (promotion is null)
            return;

        try
        {
            await promotion.SettleOutgoingAsync().ConfigureAwait(false);
        }
        finally
        {
            if (promotion.Outgoing is null && ReferenceEquals(_settling, promotion))
                _settling = null;
        }
    }

    private sealed class ActiveFade
    {
        public required ITrackTransitionHost Host { get; init; }
        public required TransitionPreparedTrack Incoming { get; init; }
        public required IPlaybackSource IncomingSource { get; init; }
        public required IPlaybackSource Outgoing { get; init; }
        public required double OutgoingVolume { get; init; }
        public required TimeSpan StartPosition { get; init; }
        public required TimeSpan HalfSpan { get; init; }
        public required Task<bool> FadeOut { get; init; }
        public PreparedPlaybackPromotion? Promotion { get; set; }
    }
}