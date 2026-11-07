using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Shell.Input;

public enum KeyboardShortcutAction
{
    TogglePlayPause,
    PreviousTrack,
    NextTrack,
    SeekBackward,
    SeekForward,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    ToggleLike,
    ToggleExpandedPlayer,
    FocusSearch,
    ToggleFullScreen
}

public sealed partial class KeyboardShortcutEntry : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string DefaultBinding { get; init; }
    public required KeyboardShortcutAction Action { get; init; }
    public bool Repeatable { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string Binding { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial bool IsCapturing { get; set; }

    public string DisplayText => IsCapturing
        ? "请按下新的快捷键…"
        : string.IsNullOrEmpty(Binding)
            ? "未设置"
            : Binding.Replace("+", " + ");

    public override string ToString()
    {
        return Name;
    }
}
