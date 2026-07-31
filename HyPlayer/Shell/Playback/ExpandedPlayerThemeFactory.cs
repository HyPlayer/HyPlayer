using System;
using Windows.UI;
using Windows.UI.Xaml.Media;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;

namespace HyPlayer.Shell.Playback;

public static class ExpandedPlayerThemeFactory
{
    public static PlaybackThemeSnapshot Create(
        UISettings uiSettings,
        LyricSettings lyricSettings,
        Color? albumMainColor,
        bool isBright)
    {
        if (uiSettings.LyricColor != LyricColor.FollowCover || albumMainColor is null)
            return isBright
                ? Create(lyricSettings, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(114, 0, 0, 0), isBright)
                : Create(lyricSettings, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(66, 255, 255, 255), isBright);

        if (uiSettings.ExpandedPlayerBackgroundType == BackgroundType.CoverBlur || isBright)
        {
            var accentColor = AdjustBrightness(albumMainColor.Value, -0.3f);
            var idleColor = accentColor;
            idleColor.A = 150;
            return Create(lyricSettings, accentColor, idleColor, isBright);
        }

        var darkAccentColor = AdjustBrightness(albumMainColor.Value, 0.3f);
        var darkIdleColor = AdjustBrightness(darkAccentColor, -0.15f);
        darkIdleColor.A = 150;
        return Create(lyricSettings, darkAccentColor, darkIdleColor, isBright);
    }

    private static PlaybackThemeSnapshot Create(
        LyricSettings settings,
        Color accentColor,
        Color idleColor,
        bool isBright)
    {
        var accentBrush = new SolidColorBrush(settings.PureLyricFocusingColor ?? accentColor);
        var idleBrush = new SolidColorBrush(settings.PureLyricIdleColor ?? idleColor);
        var karaokeAccent = settings.KaraokeLyricFocusingColor ?? accentBrush.Color;
        return new PlaybackThemeSnapshot(accentBrush, idleBrush, karaokeAccent, isBright);
    }

    private static Color AdjustBrightness(Color color, float percentage)
    {
        var adjustment = (int)(255 * percentage);
        var r = Math.Max(0, Math.Min(255, color.R + adjustment));
        var g = Math.Max(0, Math.Min(255, color.G + adjustment));
        var b = Math.Max(0, Math.Min(255, color.B + adjustment));
        return Color.FromArgb(color.A, (byte)r, (byte)g, (byte)b);
    }
}
