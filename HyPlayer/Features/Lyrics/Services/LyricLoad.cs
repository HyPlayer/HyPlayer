#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Lyrics;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Platform.Runtime.Background;

namespace HyPlayer.Features.Lyrics.Services;

internal sealed class LyricLoad
{
    private readonly object _sync = new();
    private readonly OwnedCancellation _cancellation = new();
    private volatile bool _cancelled;
    private Action<HyLyricInfo>? _publish;
    private HyLyricInfo? _latest;
    private int _attachVersion;
    private bool _sealed;
    private volatile bool _degraded;

    public LyricLoad(SingleSongBase song, Func<LyricLoad, CancellationToken, Task> resolve)
    {
        Song = song;
        var token = _cancellation.Token;
        // Always run the finally block, even if cancellation precedes worker startup.
        Completion = Task.Run(() => RunAsync(resolve, token));
    }

    private async Task RunAsync(Func<LyricLoad, CancellationToken, Task> resolve, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await resolve(this, token).ConfigureAwait(false);
        }
        finally
        {
            try { if (!token.IsCancellationRequested) Settle(); }
            finally { _cancellation.Complete(); }
        }
    }

    private void Settle()
    {
        lock (_sync)
        {
            if (_sealed || _latest is not null) return;
            _latest = new HyLyricInfo();
            _publish?.Invoke(_latest);
        }
    }

    public SingleSongBase Song { get; }

    public Task Completion { get; }

    public bool Reusable => !_degraded
                            && !_cancelled
                            && !Completion.IsFaulted
                            && !Completion.IsCanceled;

    public bool Matches(SingleSongBase song)
    {
        return ReferenceEquals(Song, song)
               || (!string.IsNullOrWhiteSpace(song.ActualId)
                   && Song.ProviderId == song.ProviderId
                   && Song.TypeId == song.TypeId
                   && Song.ActualId == song.ActualId);
    }

    public void MarkDegraded()
    {
        _degraded = true;
    }

    public void Report(HyLyricInfo info)
    {
        lock (_sync)
        {
            if (_sealed) return;
            _latest = info;
            _publish?.Invoke(info);
        }
    }

    public void Override(HyLyricInfo info)
    {
        lock (_sync)
        {
            _sealed = true;
            _latest = info;
            _publish?.Invoke(info);
        }
    }

    public int Attach(Action<HyLyricInfo> publish, Action pending)
    {
        lock (_sync)
        {
            _publish = publish;
            if (_latest is { } latest) publish(latest);
            else pending();
            return ++_attachVersion;
        }
    }

    public void Detach(int version)
    {
        lock (_sync)
        {
            if (version == _attachVersion) _publish = null;
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _publish = null;
            _attachVersion++;
        }

        _cancelled = true;
        _cancellation.Cancel();
    }
}
