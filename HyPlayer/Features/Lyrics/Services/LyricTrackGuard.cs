#nullable enable
using System.Diagnostics;
using System.Threading;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Lyrics.Services;

internal sealed class LyricTrackGuard
{
    internal const double MaxHoldMilliseconds = 1000;

    private SingleSongBase? _owner;
    private SingleSongBase? _awaited;
    private long _since;

    public void Adopt(SingleSongBase? track)
    {
        Volatile.Write(ref _owner, track);
    }

    public bool Holds(SingleSongBase? playing, long timestamp)
    {
        if (playing is null || Same(Volatile.Read(ref _owner), playing))
        {
            _awaited = null;
            return false;
        }

        if (!ReferenceEquals(_awaited, playing))
        {
            _awaited = playing;
            _since = timestamp;
        }

        return Stopwatch.GetElapsedTime(_since, timestamp).TotalMilliseconds < MaxHoldMilliseconds;
    }

    private static bool Same(SingleSongBase? owner, SingleSongBase playing)
    {
        return ReferenceEquals(owner, playing)
               || (owner is not null
                   && !string.IsNullOrWhiteSpace(playing.ActualId)
                   && owner.ProviderId == playing.ProviderId
                   && owner.TypeId == playing.TypeId
                   && owner.ActualId == playing.ActualId);
    }
}
