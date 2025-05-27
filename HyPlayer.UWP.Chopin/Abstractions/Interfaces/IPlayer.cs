using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Threading.Tasks;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface IPlayer
    {
        double Volume { get; }
        Task InitializePlayer(IAudioSettings settings);
        Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options);
        void DisconnectPlaybackSource(IPlaybackSource playbackSource);
        void PlayAll();
        void PauseAll();
        Task SeekPlaybackSourceAsync(TimeSpan target, IPlaybackSource playbackSource);
        void PausePlaybackSource(IPlaybackSource playbackSource);
        void PlayPlaybackSource(IPlaybackSource playbackSource);
        void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource);
        double GetPlaybackSourceSpeed(IPlaybackSource playbackSource);
        void SetOutputVolume(double volume);
        void SetPlaybackSourceOutputVolume(double volume, IPlaybackSource playbackSource);
        Task ChangePlayerServiceImplementation(IAudioSettings settings);
        ISMTCManager SMTCManager { get; set; }
        int ConnectedPlaybackSourceCount { get; }
    }
}
