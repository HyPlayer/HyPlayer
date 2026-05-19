using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using WinRT;

namespace HyPlayer.Services.Playback;

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
        Application.Current.Resources["SystemControlPageTextBaseHighBrush"]?.As<SolidColorBrush>()
            ?? new SolidColorBrush(Colors.White),
        Application.Current.Resources["TextFillColorTertiaryBrush"]?.As<SolidColorBrush>()
            ?? new SolidColorBrush(Colors.Gray),
        (Application.Current.Resources["SystemControlPageTextBaseHighBrush"]?.As<SolidColorBrush>())?.Color
            ?? Colors.White,
        false);
}
