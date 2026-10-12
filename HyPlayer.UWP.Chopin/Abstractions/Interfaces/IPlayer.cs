using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface IPlayer
    {
        double Volume { get; }
        Task InitializePlayer(IAudioSettings settings);
        Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options, CancellationToken cancellationToken = default);
        void DisconnectPlaybackSource(IPlaybackSource playbackSource);
        void RemoveAllPlaybackSource();
        void PlayAll();
        void PauseAll();
        void SeekPlaybackSource(TimeSpan target, IPlaybackSource playbackSource);
        void PausePlaybackSource(IPlaybackSource playbackSource);
        void PlayPlaybackSource(IPlaybackSource playbackSource);
        void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource);
        double GetPlaybackSourceSpeed(IPlaybackSource playbackSource);
        void SetPrimaryPlaybackSource(IPlaybackSource playbackSource);
        void SetOutputVolume(double volume);
        void SetPlaybackSourceOutputVolume(double volume, IPlaybackSource playbackSource);
        double GetPlaybackSourceOutputVolume(IPlaybackSource playbackSource);
        Task<bool> RampPlaybackSourceVolumeAsync(IPlaybackSource playbackSource, double target, TimeSpan duration, FadeCurve curve = FadeCurve.EqualPower);
        void PlayAll(TimeSpan fadeIn);
        Task PauseAllAsync(TimeSpan fadeOut);
        void SetPlaybackSourceAudioGain(double audioGain, IPlaybackSource playbackSource);
        Task ChangePlayerServiceImplementation(IAudioSettings settings);
        ISMTCManager SMTCManager { get; set; }
        int ConnectedPlaybackSourceCount { get; }
        PlaybackStatus GlobalPlaybackStatus { get; }
        IPlaybackSource PrimaryPlaybackSource { get; }
    }
}
