using System.Threading;
using System.Threading.Tasks;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Playback.AudioServices;

public interface IChopinPlaybackSourceResource
{
    double? SuggestedVolume { get; }

    Task<AudioGraphPlaybackSource?> CreatePlaybackSourceAsync(CancellationToken ctk = default);
}