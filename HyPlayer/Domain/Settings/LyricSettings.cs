using System;
using Windows.Storage;
using Windows.UI;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to lyric display, rendering, and behavior.
/// </summary>
public partial class LyricSettings : SettingsBase
{
    protected override string SectionName => "lyric";

    public string LyricFontFamily
    {
        get => GetSettings(nameof(LyricFontFamily), "Microsoft YaHei UI");
        set => SetSettings(nameof(LyricFontFamily), value);
    }

    /// <summary>
    ///     Romaji source for lyric transliteration.
    /// </summary>
    public RomajiSource LyricRomajiSource
    {
        get => GetSettings(nameof(LyricRomajiSource), RomajiSource.None);
        set => SetSettings(nameof(LyricRomajiSource), (int)value);
    }

    /// <summary>
    ///     Whether to show composer info in lyrics.
    /// </summary>
    public bool ShowComposerInLyric
    {
        get => GetSettings(nameof(ShowComposerInLyric), true);
        set => SetSettings(nameof(ShowComposerInLyric), value);
    }

    /// <summary>
    ///     Whether to download lyrics.
    /// </summary>
    public bool DownloadLyric
    {
        get => GetSettings(nameof(DownloadLyric), true);
        set => SetSettings(nameof(DownloadLyric), value);
    }

    /// <summary>
    ///     Whether to download translations.
    /// </summary>
    public bool DownloadTranslation
    {
        get => GetSettings(nameof(DownloadTranslation), true);
        set => SetSettings(nameof(DownloadTranslation), value);
    }

    /// <summary>
    ///     Whether to migrate lyrics format.
    /// </summary>
    public bool MigrateLyrics
    {
        get => GetSettings(nameof(MigrateLyrics), false);
        set => SetSettings(nameof(MigrateLyrics), value);
    }

    /// <summary>
    ///     Whether to optimize lyric display.
    /// </summary>
    public bool OptimizeLyric
    {
        get => GetSettings(nameof(OptimizeLyric), false);
        set => SetSettings(nameof(OptimizeLyric), value);
    }

    /// <summary>
    ///     Whether lyric drop shadow is enabled.
    /// </summary>
    public bool LyricDropShadow
    {
        get => GetSettings(nameof(LyricDropShadow), false);
        set => SetSettings(nameof(LyricDropShadow), value);
    }

    /// <summary>
    ///     Whether lyric render target caching is enabled.
    /// </summary>
    public bool LyricCacheRenderTarget
    {
        get => GetSettings(nameof(LyricCacheRenderTarget), false);
        set => SetSettings(nameof(LyricCacheRenderTarget), value);
    }

    /// <summary>
    ///     Lyric scale size.
    /// </summary>
    public int LyricScaleSize
    {
        get => GetSettings(nameof(LyricScaleSize), 3);
        set => SetSettings(nameof(LyricScaleSize), value);
    }

    /// <summary>
    ///     Lyric line spacing.
    /// </summary>
    public int LyricLineSpacing
    {
        get => GetSettings(nameof(LyricLineSpacing), 0);
        set => SetSettings(nameof(LyricLineSpacing), value);
    }

    /// <summary>
    ///     Translation font size.
    /// </summary>
    public int TranslationSize
    {
        get => GetSettings(nameof(TranslationSize), 0);
        set => SetSettings(nameof(TranslationSize), value);
    }

    /// <summary>
    ///     Romaji font size.
    /// </summary>
    public int RomajiSize
    {
        get => GetSettings(nameof(RomajiSize), 15);
        set => SetSettings(nameof(RomajiSize), value);
    }

    /// <summary>
    ///     Lyric padding top ratio.
    /// </summary>
    public int LyricPaddingTopRatio
    {
        get => GetSettings(nameof(LyricPaddingTopRatio), 30);
        set => SetSettings(nameof(LyricPaddingTopRatio), value);
    }

    /// <summary>
    ///     Lyric fading ratio.
    /// </summary>
    public int LyricFadingRatio
    {
        get => GetSettings(nameof(LyricFadingRatio), 5);
        set => SetSettings(nameof(LyricFadingRatio), value);
    }

    /// <summary>
    ///     Whether hot lyric starts on startup.
    /// </summary>
    public bool HotLyricOnStartup
    {
        get => GetSettings(nameof(HotLyricOnStartup), false);
        set => SetSettings(nameof(HotLyricOnStartup), value);
    }

    /// <summary>
    ///     Whether AMLL TTML database is enabled.
    /// </summary>
    public bool EnableAmllTtmlDb
    {
        get => GetSettings(nameof(EnableAmllTtmlDb), false);
        set => SetSettings(nameof(EnableAmllTtmlDb), value);
    }

    /// <summary>
    ///     AMLL TTML mirror URL.
    /// </summary>
    public string AmllTtmlMirrorUrl
    {
        get => GetSettings(nameof(AmllTtmlMirrorUrl),
            "https://gcore.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/ncm-lyrics/[NCM_ID].ttml");
        set => SetSettings(nameof(AmllTtmlMirrorUrl), value);
    }

    /// <summary>
    ///     Whether lyric render focus highlighting is enabled.
    /// </summary>
    public bool LyricRenderFocusHighlighting
    {
        get => GetSettings(nameof(LyricRenderFocusHighlighting), true);
        set => SetSettings(nameof(LyricRenderFocusHighlighting), value);
    }

    /// <summary>
    ///     Lyric render width ratio.
    /// </summary>
    public int LyricRenderWidthRatio
    {
        get => GetSettings(nameof(LyricRenderWidthRatio), 80);
        set => SetSettings(nameof(LyricRenderWidthRatio), value);
    }

    /// <summary>
    ///     Whether lyric render transliteration scanning is enabled.
    /// </summary>
    public bool LyricRenderTransliterationScanning
    {
        get => GetSettings(nameof(LyricRenderTransliterationScanning), true);
        set => SetSettings(nameof(LyricRenderTransliterationScanning), value);
    }

    /// <summary>
    ///     Whether lyric render simple line scanning is enabled.
    /// </summary>
    public bool LyricRenderSimpleLineScanning
    {
        get => GetSettings(nameof(LyricRenderSimpleLineScanning), false);
        set => SetSettings(nameof(LyricRenderSimpleLineScanning), value);
    }

    /// <summary>
    ///     Lyric render scan style.
    /// </summary>
    public LyricScanStyle LyricRenderScanStyle
    {
        get
        {
            var value = GetSettings(nameof(LyricRenderScanStyle), (int)LyricScanStyle.RectReveal);
            return Enum.IsDefined(typeof(LyricScanStyle), value)
                ? (LyricScanStyle)value
                : LyricScanStyle.RectReveal;
        }
        set => SetSettings(nameof(LyricRenderScanStyle), (int)value);
    }

    /// <summary>
    ///     Whether lyric render scale when focusing is enabled.
    /// </summary>
    public bool LyricRenderScaleWhenFocusing
    {
        get => GetSettings(nameof(LyricRenderScaleWhenFocusing), true);
        set => SetSettings(nameof(LyricRenderScaleWhenFocusing), value);
    }

    /// <summary>
    ///     Whether lyric render trasnform 3D when focusing is enabled.
    /// </summary>
    public bool LyricRenderTransform3D
    {
        get => GetSettings(nameof(LyricRenderTransform3D), false);
        set => SetSettings(nameof(LyricRenderTransform3D), value);
    }

    /// <summary>
    ///     Whether lyric render blur is enabled.
    /// </summary>
    public bool LyricRenderBlur
    {
        get => GetSettings(nameof(LyricRenderBlur), true);
        set => SetSettings(nameof(LyricRenderBlur), value);
    }

    /// <summary>
    ///     Whether lyric render fade is enabled.
    /// </summary>
    public bool LyricRenderFade
    {
        get => GetSettings(nameof(LyricRenderFade), true);
        set => SetSettings(nameof(LyricRenderFade), value);
    }

    /// <summary>
    ///     Line rolling calculator type.
    /// </summary>
    public RollingCalculator LineRollingCalculator
    {
        get => GetSettings(nameof(LineRollingCalculator), RollingCalculator.ElasticEaseRollingCalculator);
        set => SetSettings(nameof(LineRollingCalculator), (int)value);
    }

    /// <summary>
    ///     Whether lyric renderer debug mode is enabled.
    /// </summary>
    public bool LyricRendererDebugMode
    {
        get => GetSettings(nameof(LyricRendererDebugMode), false);
        set => SetSettings(nameof(LyricRendererDebugMode), value);
    }

#nullable enable
    /// <summary>
    ///     Pure lyric idle color override.
    /// </summary>
    public Color? PureLyricIdleColor
    {
        get
        {
            var bytes = GetSettings<byte[]?>(nameof(PureLyricIdleColor), null);
            return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
        set
        {
            var bytes = value.HasValue
                ? new[] { value.Value.A, value.Value.R, value.Value.G, value.Value.B }
                : null;
            SetSettings(nameof(PureLyricIdleColor), bytes, nameof(PureLyricIdleColor));
        }
    }

    /// <summary>
    ///     Pure lyric focusing color override.
    /// </summary>
    public Color? PureLyricFocusingColor
    {
        get
        {
            var bytes = GetSettings<byte[]?>(nameof(PureLyricFocusingColor), null);
            return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
        set
        {
            var bytes = value.HasValue
                ? new[] { value.Value.A, value.Value.R, value.Value.G, value.Value.B }
                : null;
            SetSettings(nameof(PureLyricFocusingColor), bytes, nameof(PureLyricFocusingColor));
        }
    }

    /// <summary>
    ///     Karaoke lyric focusing color override.
    /// </summary>
    public Color? KaraokeLyricFocusingColor
    {
        get
        {
            var bytes = GetSettings<byte[]?>(nameof(KaraokeLyricFocusingColor), null);
            return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
        set
        {
            var bytes = value.HasValue
                ? new[] { value.Value.A, value.Value.R, value.Value.G, value.Value.B }
                : null;
            SetSettings(nameof(KaraokeLyricFocusingColor), bytes, nameof(KaraokeLyricFocusingColor));
        }
    }
#nullable restore

    /// <summary>
    ///     Whether Isolation full throttle mode is enabled.
    /// </summary>
    public bool IsolationFullThrottle
    {
        get => GetSettings(nameof(IsolationFullThrottle), true);
        set => SetSettings(nameof(IsolationFullThrottle), value);
    }

    /// <summary>
    ///     Isolation FPS (minimum 60).
    /// </summary>
    public double IsolationFPS
    {
        get => Math.Max(GetSettings(nameof(IsolationFPS), 60d), 60d);
        set => SetSettings(nameof(IsolationFPS), value);
    }

    /// <summary>
    ///     Isolation scale factor.
    /// </summary>
    public float IsolationScale
    {
        get => GetSettings(nameof(IsolationScale), 1f);
        set => SetSettings(nameof(IsolationScale), value);
    }

    /// <summary>
    ///     Whether Isolation light wave effect is enabled.
    /// </summary>
    public bool IsolationLightWave
    {
        get => GetSettings(nameof(IsolationLightWave), false);
        set => SetSettings(nameof(IsolationLightWave), value);
    }
}
