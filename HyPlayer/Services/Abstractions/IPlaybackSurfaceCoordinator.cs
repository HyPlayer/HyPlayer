using HyPlayer.Domain.Music;
using HyPlayer.Services.Playback;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// Shell-level coordinator for the playback UI surface (ExpandedPlayer Frame, PlayBar, blur overlay).
/// Owned by MainPage; breaks the direct coupling between PlayBar and MainPage Frames.
/// External callers expand/collapse through this interface instead of casting UI state to concrete pages.
/// </summary>
public interface IPlaybackSurfaceCoordinator
{
    /// <summary>Expand the playback surface: show expanded player frame, hide main frame and blur overlay.</summary>
    void Expand();

    /// <summary>Collapse the playback surface: hide expanded player frame, restore main frame and blur overlay.</summary>
    void Collapse();

    void RefreshPlaybackCover(HyPlayItem? item);

    void StartExpandedTransition(ExpandedPlayerTransition transition);

    /// <summary>Whether the expanded player is currently shown.</summary>
    bool IsExpanded { get; }

    /// <summary>
    /// The host that manages frame-level UI orchestration. Attached by <see cref="HyPlayer.App.MainPage"/> during construction.
    /// </summary>
    IPlaybackSurfaceHost? Host { get; set; }
}
