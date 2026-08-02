namespace HyPlayer.Shell.Playback;

/// <summary>
///     Shell-level coordinator for the playback UI surface (ExpandedPlayer Frame, PlayBar, blur overlay).
///     Owned by MainPage; breaks the direct coupling between PlayBar and MainPage Frames.
///     External callers expand/collapse through this interface instead of casting UI state to concrete pages.
/// </summary>
public interface IPlaybackSurfaceCoordinator
{
    /// <summary>Whether the expanded player is currently shown.</summary>
    bool IsExpanded { get; }

    /// <summary>Expand the playback surface: show expanded player frame, hide main frame and blur overlay.</summary>
    void Expand();

    /// <summary>Collapse the playback surface: hide expanded player frame, restore main frame and blur overlay.</summary>
    void Collapse();

    void UpdateExpandedFrameOffset(double offset);

    void ResetExpandedFrameOffset();

    void RestoreExpandedSurface();
}