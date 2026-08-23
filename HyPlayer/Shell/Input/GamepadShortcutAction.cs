using Windows.System;

namespace HyPlayer.Shell.Input;

public enum GamepadShortcutAction
{
    None,
    Back,
    TogglePlayPause,
    ToggleLike,
    PreviousTrack,
    NextTrack,
    ToggleNavigationPane,
    SeekBackward,
    SeekForward
}

public static class GamepadShortcutMap
{
    public static GamepadShortcutAction Resolve(VirtualKey key, bool isPlaybackSurfaceExpanded)
    {
        return key switch
        {
            VirtualKey.GamepadB => GamepadShortcutAction.Back,
            VirtualKey.GamepadY => GamepadShortcutAction.TogglePlayPause,
            VirtualKey.GamepadX => GamepadShortcutAction.ToggleLike,
            VirtualKey.GamepadLeftShoulder => GamepadShortcutAction.PreviousTrack,
            VirtualKey.GamepadRightShoulder => GamepadShortcutAction.NextTrack,
            VirtualKey.GamepadView => GamepadShortcutAction.ToggleNavigationPane,
            VirtualKey.GamepadLeftTrigger when isPlaybackSurfaceExpanded => GamepadShortcutAction.SeekBackward,
            VirtualKey.GamepadRightTrigger when isPlaybackSurfaceExpanded => GamepadShortcutAction.SeekForward,
            _ => GamepadShortcutAction.None
        };
    }
}
