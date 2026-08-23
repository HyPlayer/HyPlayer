using System;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;

namespace HyPlayer.Shell.Input;

/// <summary>
///     Owns application-level Xbox controller accelerators while leaving A, Menu,
///     the D-pad, and the left stick to the XAML focus and context-menu systems.
/// </summary>
public sealed class GamepadShortcutService : IGamepadShortcutService
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(10);

    private readonly IAuthService _auth;
    private readonly IBackgroundTaskRunner _backgroundTasks;
    private readonly IAppNavigator _navigator;
    private readonly IPlaybackControlService _playback;
    private readonly PlaybackStateService _playbackState;
    private readonly IPlaybackSurfaceCoordinator _surface;
    private readonly DispatcherTimer _seekDelayTimer;
    private readonly DispatcherTimer _seekRepeatTimer;

    private CoreWindow? _attachedWindow;
    private VirtualKey _activeSeekKey;
    private int _seekDirection;
    private bool _seekInProgress;

    public GamepadShortcutService(
        IPlaybackControlService playback,
        PlaybackStateService playbackState,
        IAuthService auth,
        IAppNavigator navigator,
        IPlaybackSurfaceCoordinator surface,
        IBackgroundTaskRunner backgroundTasks)
    {
        _playback = playback;
        _playbackState = playbackState;
        _auth = auth;
        _navigator = navigator;
        _surface = surface;
        _backgroundTasks = backgroundTasks;

        _seekDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        _seekDelayTimer.Tick += SeekDelayTimer_Tick;
        _seekRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _seekRepeatTimer.Tick += SeekRepeatTimer_Tick;
    }

    public void Attach(CoreWindow window)
    {
        if (ReferenceEquals(_attachedWindow, window)) return;
        if (_attachedWindow is not null) Detach(_attachedWindow);

        _attachedWindow = window;
        window.KeyDown += CoreWindow_KeyDown;
        window.KeyUp += CoreWindow_KeyUp;
    }

    public void Detach(CoreWindow window)
    {
        if (!ReferenceEquals(_attachedWindow, window)) return;

        window.KeyDown -= CoreWindow_KeyDown;
        window.KeyUp -= CoreWindow_KeyUp;
        _attachedWindow = null;
        StopSeeking();
    }

    private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
    {
        var action = GamepadShortcutMap.Resolve(args.VirtualKey, _surface.IsExpanded);
        if (action == GamepadShortcutAction.None || IsInputOwnedByFocusedUi()) return;

        // Repeated KeyDown events must still be consumed, but only trigger actions once.
        args.Handled = true;
        if (args.KeyStatus.WasKeyDown) return;

        switch (action)
        {
            case GamepadShortcutAction.Back:
                if (_surface.IsExpanded)
                    _surface.Collapse();
                else
                    _navigator.NavigateBack();
                break;
            case GamepadShortcutAction.TogglePlayPause:
                _playback.TogglePlayPause();
                break;
            case GamepadShortcutAction.ToggleLike:
                _auth.LikeSong();
                break;
            case GamepadShortcutAction.PreviousTrack:
                _backgroundTasks.Forget(_playback.MovePreviousAndPlayAsync(), "gamepad previous track");
                break;
            case GamepadShortcutAction.NextTrack:
                _backgroundTasks.Forget(_playback.MoveNextAndPlayAsync(true), "gamepad next track");
                break;
            case GamepadShortcutAction.ToggleNavigationPane:
                _navigator.ToggleNavigationPane();
                break;
            case GamepadShortcutAction.SeekBackward:
                StartSeeking(args.VirtualKey, -1);
                break;
            case GamepadShortcutAction.SeekForward:
                StartSeeking(args.VirtualKey, 1);
                break;
        }
    }

    private void CoreWindow_KeyUp(CoreWindow sender, KeyEventArgs args)
    {
        if (args.VirtualKey is not (VirtualKey.GamepadLeftTrigger or VirtualKey.GamepadRightTrigger)) return;

        if (_seekDirection == 0) return;

        args.Handled = true;
        if (args.VirtualKey != _activeSeekKey) return;
        StopSeeking();
    }

    private static bool IsInputOwnedByFocusedUi()
    {
        // Let dialogs, flyouts, and focus-engaged controls consume controller input first.
        if (VisualTreeHelper.GetOpenPopups(Window.Current).Count > 0) return true;

        return FocusManager.GetFocusedElement() is Control
        {
            IsFocusEngagementEnabled: true,
            IsFocusEngaged: true
        };
    }

    private void StartSeeking(VirtualKey key, int direction)
    {
        _activeSeekKey = key;
        _seekDirection = direction;
        _seekRepeatTimer.Stop();
        _seekDelayTimer.Stop();
        _seekDelayTimer.Start();
        QueueSeekStep();
    }

    private void StopSeeking()
    {
        _activeSeekKey = VirtualKey.None;
        _seekDirection = 0;
        _seekDelayTimer.Stop();
        _seekRepeatTimer.Stop();
    }

    private void SeekDelayTimer_Tick(object? sender, object e)
    {
        _seekDelayTimer.Stop();
        if (_seekDirection == 0 || !_surface.IsExpanded) return;

        _seekRepeatTimer.Start();
        QueueSeekStep();
    }

    private void SeekRepeatTimer_Tick(object? sender, object e)
    {
        if (_seekDirection == 0 || !_surface.IsExpanded)
        {
            StopSeeking();
            return;
        }

        QueueSeekStep();
    }

    private void QueueSeekStep()
    {
        if (_seekInProgress || _seekDirection == 0) return;
        _backgroundTasks.Forget(SeekByStepAsync(), "gamepad seek");
    }

    private async System.Threading.Tasks.Task SeekByStepAsync()
    {
        _seekInProgress = true;
        try
        {
            var target = _playbackState.Position + SeekStep * _seekDirection;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (_playbackState.Duration > TimeSpan.Zero && target > _playbackState.Duration)
                target = _playbackState.Duration;

            await _playback.SeekAsync(target);
        }
        finally
        {
            _seekInProgress = false;
        }
    }
}
