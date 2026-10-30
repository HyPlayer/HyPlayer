using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Search;
using HyPlayer.Shell.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Shell.Input;

public sealed class KeyboardShortcutService : IKeyboardShortcutService
{
    private static readonly TimeSpan SeekStep = TimeSpan.FromSeconds(5);
    private const int VolumeStep = 5;

    private readonly IAuthService _auth;
    private readonly IBackgroundTaskRunner _backgroundTasks;
    private readonly IPlaybackControlService _playback;
    private readonly PlaybackStateService _playbackState;
    private readonly AudioGraphPlayer _player;
    private readonly HyPlayer.Domain.Settings.UISettings _settings;
    private readonly IShellHostStateService _shellHost;
    private readonly IPlaybackSurfaceCoordinator _surface;

    private CoreWindow? _attachedWindow;
    private KeyboardShortcutEntry? _capturing;

    public KeyboardShortcutService(
        IPlaybackControlService playback,
        PlaybackStateService playbackState,
        AudioGraphPlayer player,
        IAuthService auth,
        IPlaybackSurfaceCoordinator surface,
        IShellHostStateService shellHost,
        IBackgroundTaskRunner backgroundTasks,
        HyPlayer.Domain.Settings.UISettings settings)
    {
        _playback = playback;
        _playbackState = playbackState;
        _player = player;
        _auth = auth;
        _surface = surface;
        _shellHost = shellHost;
        _backgroundTasks = backgroundTasks;
        _settings = settings;

        Entries = CreateEntries();
    }

    public ObservableCollection<KeyboardShortcutEntry> Entries { get; }

    public event EventHandler? MuteChanged;

    public void Attach(CoreWindow window)
    {
        if (ReferenceEquals(_attachedWindow, window)) return;
        if (_attachedWindow is not null) Detach(_attachedWindow);

        _attachedWindow = window;
        window.Dispatcher.AcceleratorKeyActivated += Dispatcher_AcceleratorKeyActivated;
    }

    public void Detach(CoreWindow window)
    {
        if (!ReferenceEquals(_attachedWindow, window)) return;

        window.Dispatcher.AcceleratorKeyActivated -= Dispatcher_AcceleratorKeyActivated;
        _attachedWindow = null;
        CancelCapture();
    }

    public void BeginCapture(KeyboardShortcutEntry entry)
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = entry;
        entry.IsCapturing = true;
    }

    public void CancelCapture()
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = null;
    }

    public void Clear(KeyboardShortcutEntry entry)
    {
        entry.IsCapturing = false;
        if (ReferenceEquals(_capturing, entry)) _capturing = null;
        entry.Binding = string.Empty;
        Save();
    }

    public void ResetAll()
    {
        CancelCapture();
        foreach (var entry in Entries) entry.Binding = entry.DefaultBinding;
        Save();
    }

    private ObservableCollection<KeyboardShortcutEntry> CreateEntries()
    {
        var entries = new ObservableCollection<KeyboardShortcutEntry>
        {
            new()
            {
                Id = "playpause", Name = "播放 / 暂停", DefaultBinding = "Space",
                Action = KeyboardShortcutAction.TogglePlayPause
            },
            new()
            {
                Id = "previous", Name = "上一首", DefaultBinding = "Ctrl+Left",
                Action = KeyboardShortcutAction.PreviousTrack
            },
            new()
            {
                Id = "next", Name = "下一首", DefaultBinding = "Ctrl+Right",
                Action = KeyboardShortcutAction.NextTrack
            },
            new()
            {
                Id = "seekback", Name = "后退 5 秒", DefaultBinding = "Ctrl+Shift+Left",
                Action = KeyboardShortcutAction.SeekBackward, Repeatable = true
            },
            new()
            {
                Id = "seekforward", Name = "快进 5 秒", DefaultBinding = "Ctrl+Shift+Right",
                Action = KeyboardShortcutAction.SeekForward, Repeatable = true
            },
            new()
            {
                Id = "volumeup", Name = "音量 +5", DefaultBinding = "Ctrl+Up",
                Action = KeyboardShortcutAction.VolumeUp, Repeatable = true
            },
            new()
            {
                Id = "volumedown", Name = "音量 -5", DefaultBinding = "Ctrl+Down",
                Action = KeyboardShortcutAction.VolumeDown, Repeatable = true
            },
            new()
            {
                Id = "mute", Name = "静音", DefaultBinding = "Ctrl+M",
                Action = KeyboardShortcutAction.ToggleMute
            },
            new()
            {
                Id = "like", Name = "喜欢当前歌曲", DefaultBinding = "Ctrl+L",
                Action = KeyboardShortcutAction.ToggleLike
            },
            new()
            {
                Id = "lyrics", Name = "打开 / 收起歌词页", DefaultBinding = "Ctrl+E",
                Action = KeyboardShortcutAction.ToggleExpandedPlayer
            },
            new()
            {
                Id = "search", Name = "搜索", DefaultBinding = "Ctrl+F",
                Action = KeyboardShortcutAction.FocusSearch
            },
            new()
            {
                Id = "fullscreen", Name = "全屏", DefaultBinding = "F11",
                Action = KeyboardShortcutAction.ToggleFullScreen
            }
        };
        var saved = Parse(_settings.KeyboardShortcutBindings);
        foreach (var entry in entries)
            entry.Binding = saved.TryGetValue(entry.Id, out var binding) ? binding : entry.DefaultBinding;
        return entries;
    }

    private static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index > 0) result[pair[..index]] = pair[(index + 1)..];
        }

        return result;
    }

    private void Save()
    {
        var builder = new StringBuilder();
        foreach (var entry in Entries) builder.Append(entry.Id).Append('=').Append(entry.Binding).Append(';');
        _settings.KeyboardShortcutBindings = builder.ToString();
    }

    private void Assign(KeyboardShortcutEntry entry, string binding)
    {
        foreach (var other in Entries)
            if (!ReferenceEquals(other, entry) &&
                string.Equals(other.Binding, binding, StringComparison.OrdinalIgnoreCase))
                other.Binding = string.Empty;
        entry.Binding = binding;
        entry.IsCapturing = false;
        _capturing = null;
        Save();
    }

    private bool IsDown(VirtualKey key)
    {
        return _attachedWindow is { } window &&
               (window.GetKeyState(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private static bool IsModifier(VirtualKey key)
    {
        return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;
    }

    private static string Format(VirtualKey key, bool ctrl, bool shift, bool alt)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(key switch
        {
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
            _ => key.ToString()
        });
        return string.Join('+', parts);
    }

    private static bool IsTextInputFocused()
    {
        return FocusManager.GetFocusedElement() is TextBox or PasswordBox or RichEditBox or AutoSuggestBox;
    }

    private static bool IsEditingKey(VirtualKey key, bool ctrl, bool alt)
    {
        if (alt) return false;
        if (key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Home or VirtualKey.End or VirtualKey.Back or VirtualKey.Delete or VirtualKey.Space)
            return true;
        if (ctrl)
            return key is VirtualKey.A or VirtualKey.C or VirtualKey.V or VirtualKey.X or VirtualKey.Z
                or VirtualKey.Y;
        return key is (>= VirtualKey.A and <= VirtualKey.Z) or (>= VirtualKey.Number0 and <= VirtualKey.Number9);
    }

    private void Dispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
    {
        if (args.EventType is not (CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown))
            return;
        var key = args.VirtualKey;
        if (IsModifier(key)) return;
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);

        if (_capturing is { } capturing)
        {
            args.Handled = true;
            if (args.KeyStatus.WasKeyDown) return;
            if (key == VirtualKey.Escape && !ctrl && !shift && !alt)
            {
                CancelCapture();
                return;
            }

            Assign(capturing, Format(key, ctrl, shift, alt));
            return;
        }

        if (!_settings.KeyboardShortcutsEnabled) return;
        var binding = Format(key, ctrl, shift, alt);
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.Binding, binding, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return;
        if (IsTextInputFocused() && IsEditingKey(key, ctrl, alt)) return;
        if (args.KeyStatus.WasKeyDown && !entry.Repeatable)
        {
            args.Handled = true;
            return;
        }

        args.Handled = Execute(entry.Action);
    }

    private bool Execute(KeyboardShortcutAction action)
    {
        switch (action)
        {
            case KeyboardShortcutAction.TogglePlayPause:
                if (_playbackState.NowPlayingProviderItem is null) return false;
                _playback.TogglePlayPause();
                return true;
            case KeyboardShortcutAction.PreviousTrack:
                if (_playbackState.IsInFm)
                    PersonalFM.ExitFm();
                else
                    _backgroundTasks.Forget(_playback.MovePreviousAndPlayAsync(), "keyboard previous track");
                return true;
            case KeyboardShortcutAction.NextTrack:
                _backgroundTasks.Forget(_playback.MoveNextAndPlayAsync(true), "keyboard next track");
                return true;
            case KeyboardShortcutAction.SeekBackward:
            case KeyboardShortcutAction.SeekForward:
            {
                var target = _playbackState.Position +
                             (action == KeyboardShortcutAction.SeekForward ? SeekStep : -SeekStep);
                if (target < TimeSpan.Zero) target = TimeSpan.Zero;
                if (_playbackState.Duration > TimeSpan.Zero && target > _playbackState.Duration)
                    target = _playbackState.Duration;
                _backgroundTasks.Forget(_playback.SeekAsync(target), "keyboard seek");
                return true;
            }
            case KeyboardShortcutAction.VolumeUp:
            case KeyboardShortcutAction.VolumeDown:
            {
                var current = Math.Round(_playback.Volume * 100);
                var next = current + (action == KeyboardShortcutAction.VolumeUp ? VolumeStep : -VolumeStep);
                _playback.Volume = Math.Clamp(next, 0, 100) / 100;
                return true;
            }
            case KeyboardShortcutAction.ToggleMute:
                if (!_player.PlayerCreated) return false;
                _player.IsMuted = !_player.IsMuted;
                MuteChanged?.Invoke(this, EventArgs.Empty);
                return true;
            case KeyboardShortcutAction.ToggleLike:
                _auth.LikeSong();
                return true;
            case KeyboardShortcutAction.ToggleExpandedPlayer:
                if (_surface.IsExpanded)
                    _surface.Collapse();
                else
                    _surface.Expand();
                return true;
            case KeyboardShortcutAction.FocusSearch:
                if (_shellHost.AppTitleBar?.Content is not ShellSearchBox searchBox) return false;
                if (_surface.IsExpanded) _surface.Collapse();
                return searchBox.FocusSearch();
            case KeyboardShortcutAction.ToggleFullScreen:
            {
                var view = ApplicationView.GetForCurrentView();
                if (view.IsFullScreenMode)
                    view.ExitFullScreenMode();
                else
                    view.TryEnterFullScreenMode();
                return true;
            }
        }

        return false;
    }
}
