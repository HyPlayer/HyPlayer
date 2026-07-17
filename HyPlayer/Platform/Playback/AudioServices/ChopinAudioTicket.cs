using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Platform.Playback.AudioServices;

public sealed class ChopinAudioTicket : AudioTicketBase, IAudioTicketVolumeState
{
    public required IPlaybackSource PlaybackSource { get; init; }

    public double Volume { get; set; } = 1d;
}
