using Windows.UI;
using Windows.UI.Xaml.Media;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// Immutable snapshot of playback-relevant theme values.
/// Captures the four properties that playback UI surfaces need:
/// accent/idle brushes, karaoke accent color, and brightness flag.
/// </summary>
public readonly record struct PlaybackThemeSnapshot(
    SolidColorBrush AccentBrush,
    SolidColorBrush IdleBrush,
    Color KaraokAccentBrush,
    bool IsBright
)
{
    public static PlaybackThemeSnapshot Default => new(
        new SolidColorBrush(Colors.White),
        new SolidColorBrush(Colors.Gray),
        Colors.White,
        false);
}
