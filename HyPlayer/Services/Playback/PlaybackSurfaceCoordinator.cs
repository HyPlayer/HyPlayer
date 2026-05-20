#nullable enable
using CommunityToolkit.WinUI;
using HyPlayer.Domain.Music;
using HyPlayer.Services.Abstractions;
using HyPlayer.Shell.Playback;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;

namespace HyPlayer.Services.Playback;

/// <summary>
/// Concrete implementation of <see cref="IPlaybackSurfaceCoordinator"/>.
/// Centralizes all MainPage frame orchestration (ExpandedPlayer, MainFrame, GridPlayBar, GridPlayBarMarginBlur)
/// so that PlayBar and other callers do not directly reach into concrete pages.
///
/// Expand/collapse intent flows through <see cref="PlaybackShellStateMachine"/> which guards transitions
/// and updates <see cref="PlaybackSurfaceStore"/>. The coordinator then performs frame-level operations.
/// <see cref="IsExpanded"/> is sourced from the store.
/// </summary>
public sealed class PlaybackSurfaceCoordinator : IPlaybackSurfaceCoordinator
{
    private readonly IShellHostStateService _shellHost;
    private readonly PlaybackShellStateMachine _stateMachine;
    private readonly PlaybackSurfaceStore _surfaceStore;
    private readonly AudioGraphPlayer _player;
    private readonly PlaybackStateService _playbackState;

    public PlaybackSurfaceCoordinator(
        IShellHostStateService shellHost,
        PlaybackShellStateMachine stateMachine,
        PlaybackSurfaceStore surfaceStore,
        AudioGraphPlayer player,
        PlaybackStateService playbackState)
    {
        _shellHost = shellHost;
        _stateMachine = stateMachine;
        _surfaceStore = surfaceStore;
        _player = player;
        _playbackState = playbackState;
    }

    /// <inheritdoc />
    public IPlaybackSurfaceHost? Host { get; set; }

    /// <inheritdoc />
    public bool IsExpanded => _surfaceStore.IsExpanded;

    /// <inheritdoc />
    public void Expand()
    {
        if (Host is not { } host) return;
        if (!CanExpand()) return;

        // Guarded transition through state machine — rejects if already expanded/mid-animation
        if (!_stateMachine.TryBeginExpand())
            return;

        host.ShowExpandedPlayerFrame();
        host.NavigateExpandedPlayerFrame();
        host.SetPlayBarBorderless();
        host.HideMainFrame();
        host.HidePlayBarBlur();
        host.ClearPlayBarBackground();

        host.StartExpandedPlayerTransition(ExpandedPlayerTransition.Expand);
    }

    /// <inheritdoc />
    public void Collapse()
    {
        if (Host is not { } host) return;

        // Guarded transition through state machine — rejects if already compact/mid-animation
        if (!_stateMachine.TryBeginCollapse())
            return;

        host.IsExpandedPlayerInitialized = false;

        host.StartExpandedPlayerTransition(ExpandedPlayerTransition.Collapse);

        host.ShowPlayBarBlur();

        if (_shellHost.AppTitleBar is { } appTitleBar)
            appTitleBar.ReleasePointerCaptures();

        host.HideExpandedPlayerFrame();
        host.SetPlayBarDefaultBorder();
        host.ShowMainFrame();

        if (_shellHost.AppTitleBar is { } titleBar)
        {
            var dragRegion = titleBar.FindDescendant("PART_DragRegion")?.As<Grid>();
            Window.Current.SetTitleBar(dragRegion);
        }

        _surfaceStore.Theme = PlaybackThemeSnapshot.Default;
    }

    public void RefreshPlaybackCover(HyPlayItem? item)
    {
        Host?.RefreshPlaybackCover(item);
    }

    public void StartExpandedTransition(ExpandedPlayerTransition transition)
    {
        Host?.StartExpandedPlayerTransition(transition);
    }

    private bool CanExpand()
    {
        return _player.PlayerCreated &&
               _playbackState.NowPlayingItem?.PlayItem?.AudioGraphPlaybackSource is not null;
    }
}
