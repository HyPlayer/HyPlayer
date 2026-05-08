using System;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;

namespace HyPlayer.Classes
{
    /// <summary>
    /// Legacy cross-fade manager.
    /// <para>
    /// All cross-fade logic has been moved to <see cref="ITrackTransition"/> with the
    /// <c>"xfd"</c> (<c>CrossFadeTransition</c>) implementation.  This class is kept
    /// only for backward compatibility — it delegates every operation to the new
    /// DI-based services and should not be used in new code.
    /// </para>
    /// </summary>
    [Obsolete("Use ITrackTransition with CrossFadeTransition (\"xfd\") instead.")]
    public class FadeManager
    {
        private readonly IPlaylistService _playlist;
        private readonly PlaybackStateService _state;

        /// <summary>Whether a cross-fade is currently in progress.</summary>
        public bool Processing => _state.ActiveTransitionId == "xfd";

        /// <summary>Whether the next track has been preloaded.</summary>
        public bool Preloaded => false;

        public FadeManager()
        {
            _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
            _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        }

        /// <summary>
        /// Force-stop any in-progress fade and reset the transition.
        /// Delegates to <see cref="IPlaylistService.SetTransition"/> to switch
        /// back to direct transition, then restores cross-fade.
        /// </summary>
        public void ForceStopFadeProcess()
        {
            // Temporarily switch to direct transition to cancel any in-progress fade,
            // then switch back to cross-fade so future transitions still use it.
            _playlist.SetTransition("dir");
            _playlist.SetTransition("xfd");
        }
    }
}
