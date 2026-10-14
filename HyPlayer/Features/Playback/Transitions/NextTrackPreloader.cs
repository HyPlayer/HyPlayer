using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Features.Playback.Transitions;

internal sealed class NextTrackPreloader
{
    private const long RetryDelayMilliseconds = 8000;
    private readonly bool _startSilent;
    private Task<TransitionPreparedTrack?>? _loading;
    private CancellationTokenSource? _loadingCts;
    private IPlaybackSource? _loadingFor;
    private double _loadingRate;
    private long _retryAfter;

    public NextTrackPreloader(bool startSilent = true)
    {
        _startSilent = startSilent;
    }

    public TransitionPreparedTrack? Prepared { get; private set; }

    public async Task PrepareAsync(TrackTransitionContext context, TimeSpan leadTime, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Prepared is { } stale && !ReferenceEquals(stale.Source, context.Source))
            await DiscardAsync().ConfigureAwait(false);
        else if (_loading is not null && !ReferenceEquals(_loadingFor, context.Source))
            CancelLoading();

        if (Prepared is not null)
            return;

        if (_loading is { } pending)
        {
            if (pending.IsCompleted)
                await AdoptAsync(pending, context, ct).ConfigureAwait(false);
            return;
        }

        if (!context.CanPreload
            || context.Duration - context.Position > leadTime
            || Environment.TickCount64 < _retryAfter)
            return;

        var cts = new CancellationTokenSource();
        var loading = LoadAsync(context, cts.Token);
        _loading = loading;
        _loadingCts = cts;
        _loadingFor = context.Source;
        _loadingRate = context.PlaybackRate;
        if (loading.IsCompleted)
            await AdoptAsync(loading, context, ct).ConfigureAwait(false);
    }

    public TransitionPreparedTrack? Take()
    {
        var prepared = Prepared;
        Prepared = null;
        return prepared;
    }

    public void CancelLoading()
    {
        var loading = _loading;
        var cts = _loadingCts;
        _loading = null;
        _loadingCts = null;
        _loadingFor = null;
        if (loading is null)
            return;

        cts?.Cancel();
        _ = ReleaseAbandonedAsync(loading, cts);
    }

    public async Task DiscardAsync()
    {
        CancelLoading();
        if (Prepared is not { } prepared)
            return;

        await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        if (ReferenceEquals(Prepared, prepared))
            Prepared = null;
    }

    private async Task AdoptAsync(
        Task<TransitionPreparedTrack?> loading,
        TrackTransitionContext context,
        CancellationToken ct)
    {
        _loading = null;
        _loadingFor = null;
        _loadingCts?.Dispose();
        _loadingCts = null;
        try
        {
            var prepared = await loading.ConfigureAwait(false);
            if (prepared is not null && Math.Abs(context.PlaybackRate - _loadingRate) > 0.001)
            {
                try
                {
                    await prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
                }
                catch
                {
                    await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            Prepared = prepared;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _retryAfter = Environment.TickCount64 + RetryDelayMilliseconds;
            throw;
        }
    }

    private async Task<TransitionPreparedTrack?> LoadAsync(TrackTransitionContext context, CancellationToken ct)
    {
        var prepared = await context.Host.PrepareNextAsync(context, ct).ConfigureAwait(false);
        if (prepared is null)
            return null;

        try
        {
            await prepared.Ticket.SetPlaybackRateAsync(context.PlaybackRate, ct).ConfigureAwait(false);
            if (_startSilent)
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

        return prepared;
    }

    private static async Task ReleaseAbandonedAsync(
        Task<TransitionPreparedTrack?> loading,
        CancellationTokenSource? cts)
    {
        try
        {
            if (await loading.ConfigureAwait(false) is { } prepared)
                await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Releasing an abandoned preload failed: {ex}");
        }
        finally
        {
            cts?.Dispose();
        }
    }
}
