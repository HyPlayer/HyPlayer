#nullable enable
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// Concrete implementation of <see cref="IPlaybackSurfaceCoordinator"/>.
/// Coordinates playback surface state without holding UI element references.
///
/// Expand/collapse intent flows through <see cref="PlaybackShellStateMachine"/> which guards transitions
/// and updates <see cref="PlaybackSurfaceStore"/>. The coordinator then performs frame-level operations.
/// <see cref="IsExpanded"/> is sourced from the store.
/// </summary>
public sealed class PlaybackSurfaceCoordinator : IPlaybackSurfaceCoordinator
{
    private readonly PlaybackShellStateMachine _stateMachine;
    private readonly PlaybackSurfaceStore _surfaceStore;
    private readonly AudioGraphPlayer _player;
    private readonly PlaybackStateService _playbackState;

    public PlaybackSurfaceCoordinator(
        PlaybackShellStateMachine stateMachine,
        PlaybackSurfaceStore surfaceStore,
        AudioGraphPlayer player,
        PlaybackStateService playbackState)
    {
        _stateMachine = stateMachine;
        _surfaceStore = surfaceStore;
        _player = player;
        _playbackState = playbackState;
    }

    /// <inheritdoc />
    public bool IsExpanded => _surfaceStore.IsExpanded;

    /// <inheritdoc />
    public void Expand()
    {
        if (!CanExpand()) return;

        // Guarded transition through state machine — rejects if already expanded/mid-animation
        if (!_stateMachine.TryBeginExpand())
            return;

        _surfaceStore.RequestTransition(ExpandedPlayerTransition.Expand);
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (!_surfaceStore.IsExpanded) return;

        _surfaceStore.RequestTransition(ExpandedPlayerTransition.Collapse);

        // Guarded transition through state machine — rejects if already compact/mid-animation
        if (!_stateMachine.TryBeginCollapse())
            return;

        _surfaceStore.Theme = PlaybackThemeSnapshot.Default;
    }

    public void UpdateExpandedFrameOffset(double offset)
    {
        _surfaceStore.ExpandedFrameOffsetY = offset;
    }

    public void ResetExpandedFrameOffset()
    {
        _surfaceStore.RequestExpandedFrameReset();
    }

    public void RestoreExpandedSurface()
    {
        _surfaceStore.RequestExpandedSurfaceRestore();
    }

    private bool CanExpand()
    {
        return _player.PlayerCreated &&
               _playbackState.NowPlayingProviderItem is not null &&
               _player.PrimaryPlaybackSource is not null;
    }
}