using Windows.Media;

namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface ISMTCManager
    {
        void OnPlayAll();
        void OnPauseAll();
        void OnPositionChange(SystemMediaTransportControlsTimelineProperties position);
    }
}
