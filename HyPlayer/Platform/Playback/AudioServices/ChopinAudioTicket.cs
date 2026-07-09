using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;

namespace HyPlayer.Platform.Playback.AudioServices;

public sealed class ChopinAudioTicket : AudioTicketBase
{
    public required IPlaybackSource PlaybackSource { get; init; }

    public double Volume { get; set; } = 1d;
}
