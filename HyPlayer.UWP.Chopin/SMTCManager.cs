using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using Windows.Media;

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
        public SMTCManager(SystemMediaTransportControls SMTC)
        {
            _smtc = SMTC;
        }
    }
}
