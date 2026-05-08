using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using Windows.Media;
using Windows.Storage.Streams;

namespace HyPlayer.UWP.Chopin
{
    public class SMTCManager : ISMTCManager
    {
        private SystemMediaTransportControls _smtc;
        public void OnPauseAll()
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
        }

        public void OnPlayAll()
        {
            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        }

        public void OnPositionChange(SystemMediaTransportControlsTimelineProperties properties)
        {
            _smtc.UpdateTimelineProperties(properties);
        }

        public void UpdateDisplayInfo(string title, string artist, string albumTitle)
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = title ?? string.Empty;
            updater.MusicProperties.Artist = artist ?? string.Empty;
            updater.MusicProperties.AlbumTitle = albumTitle ?? string.Empty;

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
