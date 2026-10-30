using System;

namespace HyPlayer.Features.Playback.Services;

internal sealed class PlaybackFailureSkipLimit
{
    private readonly int _queueCountAtFirstFailure;
    private int _failures = 1;

    public PlaybackFailureSkipLimit(int queueCount)
    {
        _queueCountAtFirstFailure = queueCount;
    }

    public bool CanSkip(int queueCount)
    {
        return _failures < Math.Min(_queueCountAtFirstFailure, queueCount);
    }

    public void RecordFailure()
    {
        _failures++;
    }
}
