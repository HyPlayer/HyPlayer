using HyPlayer.Shell.Input;
using TUnit.Core;
using Windows.System;

namespace HyPlayer.Playback.Tests;

public sealed class GamepadShortcutMapTests
{
    [Test]
    [Arguments(VirtualKey.GamepadB, GamepadShortcutAction.Back)]
    [Arguments(VirtualKey.GamepadY, GamepadShortcutAction.TogglePlayPause)]
    [Arguments(VirtualKey.GamepadX, GamepadShortcutAction.ToggleLike)]
    [Arguments(VirtualKey.GamepadLeftShoulder, GamepadShortcutAction.PreviousTrack)]
    [Arguments(VirtualKey.GamepadRightShoulder, GamepadShortcutAction.NextTrack)]
    [Arguments(VirtualKey.GamepadView, GamepadShortcutAction.ToggleNavigationPane)]
    public void Global_buttons_resolve_to_expected_actions(VirtualKey key, GamepadShortcutAction expected)
    {
        EnsureEqual(GamepadShortcutMap.Resolve(key, false), expected);
    }

    [Test]
    [Arguments(VirtualKey.GamepadLeftTrigger, GamepadShortcutAction.SeekBackward)]
    [Arguments(VirtualKey.GamepadRightTrigger, GamepadShortcutAction.SeekForward)]
    public void Triggers_seek_only_when_player_is_expanded(VirtualKey key, GamepadShortcutAction expected)
    {
        EnsureEqual(GamepadShortcutMap.Resolve(key, true), expected);
        EnsureEqual(GamepadShortcutMap.Resolve(key, false), GamepadShortcutAction.None);
    }

    [Test]
    [Arguments(VirtualKey.GamepadA)]
    [Arguments(VirtualKey.GamepadMenu)]
    [Arguments(VirtualKey.GamepadDPadUp)]
    [Arguments(VirtualKey.GamepadDPadDown)]
    [Arguments(VirtualKey.GamepadDPadLeft)]
    [Arguments(VirtualKey.GamepadDPadRight)]
    public void Navigation_and_context_buttons_are_left_to_xaml(VirtualKey key)
    {
        EnsureEqual(GamepadShortcutMap.Resolve(key, true), GamepadShortcutAction.None);
    }

    private static void EnsureEqual(GamepadShortcutAction actual, GamepadShortcutAction expected)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Expected {expected}, but got {actual}.");
    }
}
