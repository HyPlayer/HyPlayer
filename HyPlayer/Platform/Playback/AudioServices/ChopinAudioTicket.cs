using System.Threading;
using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Platform.Playback.AudioServices;

public sealed class ChopinAudioTicket : AudioTicketBase, IAudioTicketVolumeState
{
    private int _disposeState;

    public required IPlaybackSource PlaybackSource { get; init; }

    public double Volume { get; set; } = 1d;

    internal bool TryBeginDispose()
    {
        return Interlocked.CompareExchange(ref _disposeState, 1, 0) == 0;
    }

    internal void CompleteDispose()
    {
        Volatile.Write(ref _disposeState, 2);
    }

    internal void CancelDispose()
    {
        Interlocked.CompareExchange(ref _disposeState, 0, 1);
    }
}