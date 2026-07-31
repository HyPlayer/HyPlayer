using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
///     Centralized playback surface state for compact/expanded shell presentation.
///     Playback state itself remains owned by <see cref="PlaybackStateService" />.
/// </summary>
public partial class PlaybackSurfaceStore : ObservableObject
{
    /// <summary>The current playback surface mode.</summary>
    [ObservableProperty]
    public partial PlaybackSurfaceMode SurfaceMode { get; set; }

    [ObservableProperty] public partial PlaybackThemeSnapshot Theme { get; set; } = PlaybackThemeSnapshot.Default;

    [ObservableProperty] public partial ExpandedPlayerTransition RequestedTransition { get; set; }

    [ObservableProperty] public partial long TransitionRequestId { get; set; }

    [ObservableProperty] public partial double ExpandedFrameOffsetY { get; set; }

    [ObservableProperty] public partial long ExpandedFrameResetRequestId { get; set; }

    [ObservableProperty] public partial long ExpandedSurfaceRestoreRequestId { get; set; }

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
    ///     Reset all projections to collapsed/compact defaults.
    ///     Call when the playback surface is torn down.
    /// </summary>
    public void ResetToCompact()
    {
        HasPendingExpandedIntent = false;
        SurfaceMode = PlaybackSurfaceMode.Compact;
    }

    /// <summary>
    ///     Remember that activation wants the expanded surface once a host and playable item are ready.
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
