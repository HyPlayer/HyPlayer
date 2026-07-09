using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// Centralized playback surface state for compact/expanded shell presentation.
/// Playback state itself remains owned by <see cref="PlaybackStateService"/>.
/// </summary>
public partial class PlaybackSurfaceStore : ObservableObject
{
    /// <summary>The current playback surface mode.</summary>
    [ObservableProperty]
    public partial PlaybackSurfaceMode SurfaceMode { get; set; }

    [ObservableProperty]
    public partial PlaybackThemeSnapshot Theme { get; set; } = PlaybackThemeSnapshot.Default;

    [ObservableProperty]
    public partial ExpandedPlayerTransition RequestedTransition { get; set; }

    [ObservableProperty]
    public partial long TransitionRequestId { get; set; }

    [ObservableProperty]
    public partial double ExpandedFrameOffsetY { get; set; }

    [ObservableProperty]
    public partial long ExpandedFrameResetRequestId { get; set; }

    [ObservableProperty]
    public partial long ExpandedSurfaceRestoreRequestId { get; set; }

    /// <summary>Convenience: true when the surface is in Expanded mode.</summary>
    public bool IsExpanded => SurfaceMode == PlaybackSurfaceMode.Expanded;

    /// <summary>True when launch/activation requested expansion before a playback surface host was ready.</summary>
    public bool HasPendingExpandedIntent { get; private set; }

    /// <summary>Projection of playback surface state relevant to the compact PlayBar.</summary>
    public PlayBarSurfaceProjection PlayBarProjection { get; } = new();

    /// <summary>Projection of playback surface state relevant to the expanded player.</summary>
    public ExpandedPlayerSurfaceProjection ExpandedProjection { get; } = new();

    partial void OnSurfaceModeChanged(PlaybackSurfaceMode value)
    {
        var expanded = value == PlaybackSurfaceMode.Expanded;
        HasPendingExpandedIntent = false;
        OnPropertyChanged(nameof(IsExpanded));

        PlayBarProjection.IsExpanded = expanded;
        ExpandedProjection.IsActive = expanded;
    }

    /// <summary>
    /// Reset all projections to collapsed/compact defaults.
    /// Call when the playback surface is torn down.
    /// </summary>
    public void ResetToCompact()
    {
        HasPendingExpandedIntent = false;
        SurfaceMode = PlaybackSurfaceMode.Compact;
    }

    /// <summary>
    /// Remember that activation wants the expanded surface once a host and playable item are ready.
    /// </summary>
    public void RestoreExpandedIntent()
    {
        HasPendingExpandedIntent = true;
    }

    public void RequestTransition(ExpandedPlayerTransition transition)
    {
        RequestedTransition = transition;
        TransitionRequestId++;
    }

    public void RequestExpandedFrameReset()
    {
        ExpandedFrameResetRequestId++;
    }

    public void RequestExpandedSurfaceRestore()
    {
        ExpandedSurfaceRestoreRequestId++;
    }
}

/// <summary>High-level playback surface mode.</summary>
public enum PlaybackSurfaceMode
{
    /// <summary>Compact PlayBar visible; expanded player hidden.</summary>
    Compact,

    /// <summary>Expanded player fills the shell; PlayBar controls suppressed.</summary>
    Expanded
}

/// <summary>
/// Projection of PlayBar-specific visibility booleans derived from <see cref="PlaybackSurfaceStore"/>.
/// </summary>
public sealed class PlayBarSurfaceProjection : ObservableObject
{
    private bool _isExpanded;

    /// <summary>
    /// True when the playback surface is in Expanded mode.
    /// When true: expand button hidden, collapse button visible,
    /// song info hidden, advanced operations visible.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ShowExpandButton));
                OnPropertyChanged(nameof(ShowCollapseButton));
                OnPropertyChanged(nameof(ShowSongInfo));
                OnPropertyChanged(nameof(ShowAdvancedOperations));
            }
        }
    }

    /// <summary>Expand button is visible only when collapsed.</summary>
    public bool ShowExpandButton => !IsExpanded;

    /// <summary>Collapse button is visible only when expanded.</summary>
    public bool ShowCollapseButton => IsExpanded;

    /// <summary>Song info (title/artist/album) is visible only when collapsed.</summary>
    public bool ShowSongInfo => !IsExpanded;

    /// <summary>Advanced operations (like/download/comment) are visible only when expanded.</summary>
    public bool ShowAdvancedOperations => IsExpanded;
}

/// <summary>
/// Projection of expanded-player-specific state derived from <see cref="PlaybackSurfaceStore"/>.
/// </summary>
public sealed class ExpandedPlayerSurfaceProjection : ObservableObject
{
    private bool _isActive;

    /// <summary>True when the expanded playback surface is active / visible.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
