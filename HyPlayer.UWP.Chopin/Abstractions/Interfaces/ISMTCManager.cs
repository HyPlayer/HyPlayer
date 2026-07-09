using Windows.Media;
using Windows.Storage.Streams;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface ISMTCManager
    {
        void OnPlayAll();
        void OnPauseAll();
        void UpdatePlaybackStatus(PlaybackStatus status);
        void OnPositionChange(SystemMediaTransportControlsTimelineProperties position);
        void UpdateDisplayInfo(string title, string artist, string albumTitle);
        void UpdateThumbnail(RandomAccessStreamReference thumbnail);
    }
}
