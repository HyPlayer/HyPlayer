using System;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// Abstraction over MainPage frame controls, attached by MainPage so that
/// <see cref="IPlaybackSurfaceCoordinator"/> and <see cref="Pages.ExpandedPlayer"/>
/// can manipulate frame-level UI without reaching into concrete pages.
/// </summary>
public interface IPlaybackSurfaceHost
{
    // ── Frame orchestration (used by coordinator) ──

    /// <summary>Show the ExpandedPlayer frame and make it visible.</summary>
    void ShowExpandedPlayerFrame();

    /// <summary>Navigate the ExpandedPlayer frame to <paramref name="pageType"/>.</summary>
    void NavigateExpandedPlayerFrame();

    /// <summary>Navigate the ExpandedPlayer frame to <see cref="Pages.BlankPage"/> and hide it.</summary>
    void HideExpandedPlayerFrame();

    /// <summary>Show the main navigation frame.</summary>
    void ShowMainFrame();

    /// <summary>Hide the main navigation frame.</summary>
    void HideMainFrame();

    /// <summary>Remove the play-bar border (set thickness to zero).</summary>
    void SetPlayBarBorderless();

    /// <summary>Restore the default play-bar border and acrylic background.</summary>
    void SetPlayBarDefaultBorder();

    /// <summary>Clear the play-bar background brush.</summary>
    void ClearPlayBarBackground();

    /// <summary>Show the play-bar margin blur overlay.</summary>
    void ShowPlayBarBlur();

    /// <summary>Hide the play-bar margin blur overlay.</summary>
    void HidePlayBarBlur();

    // ── State helpers (used by ExpandedPlayer / coordinator) ──
    bool IsExpandedPlayerInitialized { get; set; }

    /// <summary>Set the ExpandedPlayer frame vertical translation offset (gesture tracking).</summary>
    void SetExpandedPlayerFrameOffsetY(double offset);

    /// <summary>Start the animation that resets the ExpandedPlayer frame offset to zero.</summary>
    void BeginImageResetAnimation();
}
