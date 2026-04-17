using HyPlayer.Classes;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;
using Windows.UI;

namespace HyPlayer.Classes.Settings
{
    /// <summary>
    /// Settings related to lyric display, rendering, and behavior.
    /// </summary>
    public class LyricSettings : SettingsBase
    {
        /// <summary>
        /// Romaji source for lyric transliteration.
        /// </summary>
        public RomajiSource LyricRomajiSource
        {
            get => GetSettings(nameof(LyricRomajiSource), RomajiSource.None);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRomajiSource)] = (int)value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether high-precision lyric timer is enabled.
        /// </summary>
        public bool highPreciseLyricTimer
        {
            get => GetSettings(nameof(highPreciseLyricTimer), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(highPreciseLyricTimer)] = value;
        }

        /// <summary>
        /// Whether karaoke lyric mode is enabled.
        /// </summary>
        public bool karaokLyric
        {
            get => GetSettings(nameof(karaokLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(karaokLyric)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to show composer info in lyrics.
        /// </summary>
        public bool showComposerInLyric
        {
            get => GetSettings(nameof(showComposerInLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(showComposerInLyric)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to download lyrics.
        /// </summary>
        public bool downloadLyric
        {
            get => GetSettings(nameof(downloadLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadLyric)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to download translations.
        /// </summary>
        public bool downloadTranslation
        {
            get => GetSettings(nameof(downloadTranslation), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadTranslation)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to migrate lyrics format.
        /// </summary>
        public bool MigrateLyrics
        {
            get => GetSettings(nameof(MigrateLyrics), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(MigrateLyrics)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to optimize lyric display.
        /// </summary>
        public bool OptimizeLyric
        {
            get => GetSettings(nameof(OptimizeLyric), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(OptimizeLyric)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric drop shadow is enabled.
        /// </summary>
        public bool lyricDropshadow
        {
            get => GetSettings(nameof(lyricDropshadow), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(lyricDropshadow)] = value;
        }

        /// <summary>
        /// Whether lyric render target caching is enabled.
        /// </summary>
        public bool lyricCacheRenderTarget
        {
            get => GetSettings(nameof(lyricCacheRenderTarget), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricCacheRenderTarget)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric scale size.
        /// </summary>
        public int lyricScaleSize
        {
            get => GetSettings(nameof(lyricScaleSize), 3);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricScaleSize)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric font family.
        /// </summary>
        public string lyricFontFamily
        {
            get => GetSettings(nameof(lyricFontFamily), "Microsoft YaHei UI");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricFontFamily)] = value;
            }
        }

        /// <summary>
        /// Lyric line spacing.
        /// </summary>
        public int lyricLineSpacing
        {
            get => GetSettings(nameof(lyricLineSpacing), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricLineSpacing)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Translation font size.
        /// </summary>
        public int translationSize
        {
            get => GetSettings(nameof(translationSize), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(translationSize)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Romaji font size.
        /// </summary>
        public int romajiSize
        {
            get => GetSettings(nameof(romajiSize), 15);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(romajiSize)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric padding top ratio.
        /// </summary>
        public int lyricPaddingTopRatio
        {
            get => GetSettings(nameof(lyricPaddingTopRatio), 30);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricPaddingTopRatio)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric fading ratio.
        /// </summary>
        public int lyricFadingRatio
        {
            get => GetSettings(nameof(lyricFadingRatio), 5);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricFadingRatio)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether hot lyric starts on startup.
        /// </summary>
        public bool hotlyricOnStartup
        {
            get => GetSettings(nameof(hotlyricOnStartup), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(hotlyricOnStartup)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether AMLL TTML database is enabled.
        /// </summary>
        public bool enableAmllTtmlDb
        {
            get => GetSettings(nameof(enableAmllTtmlDb), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableAmllTtmlDb)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// AMLL TTML mirror URL.
        /// </summary>
        public string amllTtmlMirrorUrl
        {
            get => GetSettings(nameof(amllTtmlMirrorUrl), "https://gcore.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/ncm-lyrics/[NCM_ID].ttml");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(amllTtmlMirrorUrl)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render focus highlighting is enabled.
        /// </summary>
        public bool lyricRenderFocusHighlighting
        {
            get => GetSettings(nameof(lyricRenderFocusHighlighting), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderFocusHighlighting)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric render width ratio.
        /// </summary>
        public int lyricRenderWidthRatio
        {
            get => GetSettings(nameof(lyricRenderWidthRatio), 80);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderWidthRatio)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render transliteration scanning is enabled.
        /// </summary>
        public bool lyricRenderTransliterationScanning
        {
            get => GetSettings(nameof(lyricRenderTransliterationScanning), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderTransliterationScanning)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render simple line scanning is enabled.
        /// </summary>
        public bool lyricRenderSimpleLineScanning
        {
            get => GetSettings(nameof(lyricRenderSimpleLineScanning), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderSimpleLineScanning)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render scale when focusing is enabled.
        /// </summary>
        public bool lyricRenderScaleWhenFocusing
        {
            get => GetSettings(nameof(lyricRenderScaleWhenFocusing), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderScaleWhenFocusing)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render blur is enabled.
        /// </summary>
        public bool lyricRenderBlur
        {
            get => GetSettings(nameof(lyricRenderBlur), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderBlur)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric render fade is enabled.
        /// </summary>
        public bool lyricRenderFade
        {
            get => GetSettings(nameof(lyricRenderFade), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderFade)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Line rolling calculator type.
        /// </summary>
        public RollingCalculator LineRollingCalculator
        {
            get => GetSettings(nameof(LineRollingCalculator), RollingCalculator.ElasticEaseRollingCalculator);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LineRollingCalculator)] = (int)value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether lyric renderer debug mode is enabled.
        /// </summary>
        public bool LyricRendererDebugMode
        {
            get => GetSettings(nameof(LyricRendererDebugMode), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRendererDebugMode)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Lyric renderer FPS.
        /// </summary>
        public int LyricRendererFPS
        {
            get => GetSettings(nameof(LyricRendererFPS), 60);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRendererFPS)] = value;
                OnPropertyChanged();
            }
        }

#nullable enable
        /// <summary>
        /// Pure lyric idle color override.
        /// </summary>
        public Color? pureLyricIdleColor
        {
            get
            {
                var bytes = GetSettings<byte[]?>(nameof(pureLyricIdleColor), null);
                return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
            }
            set
            {
                if (value.HasValue)
                    ApplicationData.Current.LocalSettings.Values[nameof(pureLyricIdleColor)] = new[]
                        { value.Value.A, value.Value.R, value.Value.G, value.Value.B };
                else ApplicationData.Current.LocalSettings.Values[nameof(pureLyricIdleColor)] = null;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Pure lyric focusing color override.
        /// </summary>
        public Color? pureLyricFocusingColor
        {
            get
            {
                var bytes = GetSettings<byte[]?>(nameof(pureLyricFocusingColor), null);
                return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
            }
            set
            {
                if (value.HasValue)
                    ApplicationData.Current.LocalSettings.Values[nameof(pureLyricFocusingColor)] = new[]
                        { value.Value.A, value.Value.R, value.Value.G, value.Value.B };
                else ApplicationData.Current.LocalSettings.Values[nameof(pureLyricFocusingColor)] = null;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Karaoke lyric focusing color override.
        /// </summary>
        public Color? karaokLyricFocusingColor
        {
            get
            {
                var bytes = GetSettings<byte[]?>(nameof(karaokLyricFocusingColor), null);
                return bytes == null ? null : Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]);
            }
            set
            {
                if (value.HasValue)
                    ApplicationData.Current.LocalSettings.Values[nameof(karaokLyricFocusingColor)] = new[]
                        { value.Value.A, value.Value.R, value.Value.G, value.Value.B };
                else ApplicationData.Current.LocalSettings.Values[nameof(karaokLyricFocusingColor)] = null;
                OnPropertyChanged();
            }
        }
#nullable restore

        /// <summary>
        /// Whether Isolation full throttle mode is enabled.
        /// </summary>
        public bool IsolationFullThrottle
        {
            get => GetSettings(nameof(IsolationFullThrottle), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationFullThrottle)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Isolation FPS (minimum 60).
        /// </summary>
        public double IsolationFPS
        {
            get => System.Math.Max(GetSettings(nameof(IsolationFPS), 60d), 60d);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationFPS)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Isolation scale factor.
        /// </summary>
        public float IsolationScale
        {
            get => GetSettings(nameof(IsolationScale), 1f);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationScale)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether Isolation light wave effect is enabled.
        /// </summary>
        public bool IsolationLightWave
        {
            get => GetSettings(nameof(IsolationLightWave), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationLightWave)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether Impressionist uses LAB color space.
        /// </summary>
        public bool ImpressionistLABSpace
        {
            get => GetSettings(nameof(ImpressionistLABSpace), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistLABSpace)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether Impressionist ignores white colors.
        /// </summary>
        public bool ImpressionistIgnoreWhite
        {
            get => GetSettings(nameof(ImpressionistIgnoreWhite), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistIgnoreWhite)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether Impressionist uses KMeans++ algorithm.
        /// </summary>
        public bool ImpressionistUseKMeansPP
        {
            get => GetSettings(nameof(ImpressionistUseKMeansPP), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistUseKMeansPP)] = value;
                OnPropertyChanged();
            }
        }
    }
}
