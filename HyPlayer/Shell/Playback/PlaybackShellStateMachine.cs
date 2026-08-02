using HyPlayer.Features.Playback.Services;

namespace HyPlayer.Shell.Playback;

/// <summary>
///     Shell-level state machine for playback surface transitions.
///     Owns the canonical playback shell state and enforces valid transitions
///     between Compact ↔ ExpandAnimating ↔ Expanded states.
///     Updates <see cref="PlaybackSurfaceStore" /> to keep UI projections in sync.
///     Intended to sit above <c>PlaybackSurfaceCoordinator</c> so that higher-level
///     expand/collapse intent flows through the state machine and store before
///     frame-level orchestration executes.
/// </summary>
public class PlaybackShellStateMachine
{
    private readonly PlaybackSurfaceStore _surfaceStore;

    public PlaybackShellStateMachine(PlaybackSurfaceStore surfaceStore)
    {
        _surfaceStore = surfaceStore;
    }

    /// <summary>Current shell playback state.</summary>
    public PlaybackShellState CurrentState { get; private set; } = PlaybackShellState.Compact;

    /// <summary>
    ///     Guarded transition from Compact → ExpandAnimating → Expanded.
    ///     Returns false if the current state does not allow an expand (e.g. already expanded or mid-animation).
    /// </summary>
    public bool TryBeginExpand()
    {
        if (CurrentState is PlaybackShellState.ExpandAnimating or PlaybackShellState.Expanded)
            return false;

        TransitionTo(PlaybackShellState.ExpandAnimating);
        _surfaceStore.SurfaceMode = PlaybackSurfaceMode.Expanded;
        TransitionTo(PlaybackShellState.Expanded);
        return true;
    }

    /// <summary>
    ///     Guarded transition from Expanded → CollapseAnimating → Compact.
    ///     Returns false if the current state does not allow a collapse.
    /// </summary>
    public bool TryBeginCollapse()
    {
        if (CurrentState is PlaybackShellState.CollapseAnimating or PlaybackShellState.Compact)
            return false;

        TransitionTo(PlaybackShellState.CollapseAnimating);
        _surfaceStore.SurfaceMode = PlaybackSurfaceMode.Compact;
        TransitionTo(PlaybackShellState.Compact);
        return true;
    }

    /// <summary>
    ///     Force-reset to Compact, bypassing animation transient states.
    ///     Useful during shell shutdown or navigation away from playback.
    /// </summary>
    public void ForceCompact()
    {
        _surfaceStore.ResetToCompact();
        TransitionTo(PlaybackShellState.Compact);
    }

    private void TransitionTo(PlaybackShellState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
    }
}

/// <summary>
///     Shell-level playback states.
///     Idle states (Compact, Expanded) represent terminal / settled states.
///     Transient states (ExpandAnimating, CollapseAnimating) represent mid-animation phases
///     during which further expand/collapse requests are rejected.
/// </summary>
public enum PlaybackShellState
{
    /// <summary>Compact PlayBar mode — the settled idle state when collapsed.</summary>
    Compact,

    /// <summary>Expanded player is animating open. Rejects further expand/collapse.</summary>
    ExpandAnimating,

    /// <summary>Expanded player is fully visible — the settled idle state when expanded.</summary>
    Expanded,

    /// <summary>Expanded player is animating closed. Rejects further expand/collapse.</summary>
    CollapseAnimating
}