using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using Windows.Media;
using Windows.Storage.Streams;

#nullable enable

namespace HyPlayer.UWP.Chopin
{
    public class SMTCManager : ISMTCManager
    {
        private SystemMediaTransportControls _smtc;
        public void OnPauseAll()
        {
            UpdatePlaybackStatus(PlaybackStatus.Paused);
        }

        public void OnPlayAll()
        {
            UpdatePlaybackStatus(PlaybackStatus.Playing);
        }

        public void UpdatePlaybackStatus(PlaybackStatus status)
        {
            _smtc.PlaybackStatus = status switch
            {
                PlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                PlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                PlaybackStatus.Closed => MediaPlaybackStatus.Closed,
                _ => MediaPlaybackStatus.Closed
            };
        }

        public void OnPositionChange(SystemMediaTransportControlsTimelineProperties properties)
        {
            _smtc.UpdateTimelineProperties(properties);
        }

        public void UpdateDisplayInfo(string title, string artist, string albumTitle, string? trackIdentity)
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = title ?? string.Empty;
            updater.MusicProperties.Artist = artist ?? string.Empty;
            updater.MusicProperties.AlbumTitle = albumTitle ?? string.Empty;
            updater.MusicProperties.Genres.Clear();
            if (!string.IsNullOrWhiteSpace(trackIdentity))
            {
                updater.MusicProperties.Genres.Add(trackIdentity);
            }

            updater.Update();
        }

        public void UpdateThumbnail(RandomAccessStreamReference thumbnail)
        {
            if (thumbnail is null) return;

            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.Thumbnail = thumbnail;

            updater.Update();
        }

        public SMTCManager(SystemMediaTransportControls SMTC)
        {
            _smtc = SMTC ?? throw new ArgumentNullException(nameof(SMTC)); ;
        }
    }
}
