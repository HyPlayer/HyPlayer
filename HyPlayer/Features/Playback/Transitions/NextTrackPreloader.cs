using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Transitions;

internal sealed class NextTrackPreloader
{
    private static readonly long _retryDelayTicks = Stopwatch.Frequency * 8;
    private long _retryAfter;

    public TransitionPreparedTrack? Prepared { get; private set; }

    public async Task PrepareAsync(TrackTransitionContext context, TimeSpan leadTime, CancellationToken ct)
    {
        if (Prepared is { } stale && !ReferenceEquals(stale.Source, context.Source))
            await DiscardAsync().ConfigureAwait(false);

        if (Prepared is not null
            || !context.CanPreload
            || context.Duration - context.Position > leadTime
            || Stopwatch.GetTimestamp() < _retryAfter)
            return;

        TransitionPreparedTrack? prepared;
        try
        {
            prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _retryAfter = Stopwatch.GetTimestamp() + _retryDelayTicks;
            throw;
        }

        if (prepared is null)
        {
            _retryAfter = Stopwatch.GetTimestamp() + _retryDelayTicks;
            return;
        }

        try
        {
            await prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
            await prepared.Ticket.SetVolumeAsync(0, ct).ConfigureAwait(false);
        }
        catch (Exception effectException)
        {
            try
            {
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(effectException, cleanupException);
            }

            throw;
        }

        Prepared = prepared;
    }

    public TransitionPreparedTrack? Take()
    {
        var prepared = Prepared;
        Prepared = null;
        return prepared;
    }

    public async Task DiscardAsync()
    {
        if (Prepared is not { } prepared)
            return;

        await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        if (ReferenceEquals(Prepared, prepared))
            Prepared = null;
    }
}
