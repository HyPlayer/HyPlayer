using Windows.Media;
using Windows.Storage.Streams;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface ISMTCManager
    {
        void OnPlayAll();
        void OnPauseAll();
        void OnPositionChange(SystemMediaTransportControlsTimelineProperties position);
        void UpdateDisplayInfo(string title, string artist, string albumTitle);
        void UpdateThumbnail(RandomAccessStreamReference thumbnail);
    }
}
