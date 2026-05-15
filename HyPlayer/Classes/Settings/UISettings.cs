using HyPlayer.Classes;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;

using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
namespace HyPlayer.Classes.Settings
{
    /// <summary>
    /// Settings related to UI appearance, themes, animations, and visual effects.
    /// </summary>
    public partial class UISettings : SettingsBase
    {
        /// <summary>
        /// Theme request (Auto, Light, Dark).
        /// </summary>
        public ThemeRequest themeRequest
        {
            get => GetSettings(nameof(themeRequest), ThemeRequest.Auto);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(themeRequest)] = (int)value;
            }
        }

        /// <summary>
        /// Whether expand animation is enabled.
        /// </summary>
        public bool expandAnimation
        {
            get => GetSettings(nameof(expandAnimation), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandAnimation)] = value ? "true" : "false";
            }
        }

        /// <summary>
        /// Whether to disable image loading.
        /// </summary>
        public bool noImage
        {
            get => GetSettings(nameof(noImage), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(noImage)] = value;
        }

        /// <summary>
        /// Lyric alignment setting.
        /// </summary>
        public LyricAlignment lyricAlignment
        {
            get => GetSettings(nameof(lyricAlignment), LyricAlignment.Left);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricAlignment)] = (int)value;
            }
        }

        /// <summary>
        /// Lyric font size override (0 = default).
        /// </summary>
        public int lyricSize
        {
            get => GetSettings(nameof(lyricSize), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricSize)] = value;
            }
        }

        /// <summary>
        /// Lyric color mode.
        /// </summary>
        public LyricColor lyricColor
        {
            get => GetSettings(nameof(lyricColor), LyricColor.Auto);
            set => ApplicationData.Current.LocalSettings.Values[nameof(lyricColor)] = (int)value;
        }

        /// <summary>
        /// Color generator type for theming.
        /// </summary>
        public ColorGeneratorType ColorGeneratorType
        {
            get => GetSettings(nameof(ColorGeneratorType), ColorGeneratorType.Auto);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ColorGeneratorType)] = (int)value;
            }
        }

        /// <summary>
        /// Expanded player background type.
        /// </summary>
        public BackgroundType expandedPlayerBackgroundType
        {
            get => GetSettings(nameof(expandedPlayerBackgroundType), BackgroundType.CoverBlur);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedPlayerBackgroundType)] = (int)value;
            }
        }

        /// <summary>
        /// Whether album cover rotates during playback.
        /// </summary>
        public bool albumRotate
        {
            get => GetSettings(nameof(albumRotate), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(albumRotate)] = value;
                if (value) albumRound = true;
            }
        }

        /// <summary>
        /// Whether album cover is displayed as round.
        /// </summary>
        public bool albumRound
        {
            get => GetSettings(nameof(albumRound), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(albumRound)] = value;
                if (!value) albumRotate = false;
            }
        }

        /// <summary>
        /// Album border length.
        /// </summary>
        public int albumBorderLength
        {
            get => GetSettings(nameof(albumBorderLength), 0);
            set => ApplicationData.Current.LocalSettings.Values[nameof(albumBorderLength)] = value;
        }

        /// <summary>
        /// Whether expanded player uses acrylic.
        /// </summary>
        public bool expandedUseAcrylic
        {
            get => GetSettings(nameof(expandedUseAcrylic), true);
            set => ApplicationData.Current.LocalSettings.Values[nameof(expandedUseAcrylic)] = value;
        }

        /// <summary>
        /// Whether expanded album has breath animation.
        /// </summary>
        public bool expandAlbumBreath
        {
            get => GetSettings(nameof(expandAlbumBreath), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(expandAlbumBreath)] = value;
        }

        /// <summary>
        /// Whether expanded player shows full cover.
        /// </summary>
        public bool expandedPlayerFullCover
        {
            get => GetSettings(nameof(expandedPlayerFullCover), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedPlayerFullCover)] = value;
            }
        }

        /// <summary>
        /// Expanded cover shadow depth.
        /// </summary>
        public int expandedCoverShadowDepth
        {
            get => GetSettings(nameof(expandedCoverShadowDepth), 4);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedCoverShadowDepth)] = value;
            }
        }

        /// <summary>
        /// Whether compact player page blur is enabled.
        /// </summary>
        public bool CompactPlayerPageBlurStatus
        {
            get => GetSettings(nameof(CompactPlayerPageBlurStatus), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(CompactPlayerPageBlurStatus)] = value;
            }
        }

        /// <summary>
        /// Whether not-clear mode is enabled.
        /// </summary>
        public bool notClearMode
        {
            get => GetSettings(nameof(notClearMode), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(notClearMode)] = value;
            }
        }

        /// <summary>
        /// Whether playbar auto-hides.
        /// </summary>
        public bool AutoHidePlaybar
        {
            get => GetSettings(nameof(AutoHidePlaybar), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(AutoHidePlaybar)] = value;
            }
        }

        /// <summary>
        /// Auto-hide playbar timeout in seconds.
        /// </summary>
        public int AutoHidePlaybarTime
        {
            get => GetSettings(nameof(AutoHidePlaybarTime), 3);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(AutoHidePlaybarTime)] = value;
                Ioc.Default.GetRequiredService<IUIStateService>().PlaybarSecondCounter = 0;
            }
        }

        /// <summary>
        /// Whether playbar has margin.
        /// </summary>
        public bool playBarMargin
        {
            get => GetSettings(nameof(playBarMargin), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playBarMargin)] = value;
            }
        }

        /// <summary>
        /// Whether UI sounds are enabled.
        /// </summary>
        public bool uiSound
        {
            get => GetSettings(nameof(uiSound), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(uiSound)] = value;
            }
        }

        /// <summary>
        /// Whether to display shuffled list.
        /// </summary>
        public bool displayShuffledList
        {
            get => GetSettings(nameof(displayShuffledList), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(displayShuffledList)] = value;
            }
        }

        /// <summary>
        /// Whether to display maintenance info.
        /// </summary>
        public bool displayMaintain
        {
            get => GetSettings(nameof(displayMaintain), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(displayMaintain)] = value;
            }
        }

        /// <summary>
        /// Whether touch gesture actions are enabled.
        /// </summary>
        public bool enableTouchGestureAction
        {
            get => GetSettings(nameof(enableTouchGestureAction), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(enableTouchGestureAction)] = value;
        }

        /// <summary>
        /// Gesture mode.
        /// </summary>
        public GestureMode gestureMode
        {
            get => GetSettings(nameof(gestureMode), GestureMode.Basic);
            set => ApplicationData.Current.LocalSettings.Values[nameof(gestureMode)] = (int)value;
        }

        /// <summary>
        /// Whether animation adapts to BPM.
        /// </summary>
        public bool animationAdaptBPM
        {
            get => GetSettings(nameof(animationAdaptBPM), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(animationAdaptBPM)] = value;
            }
        }

        /// <summary>
        /// Whether gentle BPM animation is enabled.
        /// </summary>
        public bool gentleBPMAnimation
        {
            get => GetSettings(nameof(gentleBPMAnimation), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(gentleBPMAnimation)] = value;
            }
        }

        /// <summary>
        /// Whether popup notifications are disabled.
        /// </summary>
        public bool DisablePopUp
        {
            get => GetSettings(nameof(DisablePopUp), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(DisablePopUp)] = value;
        }

        /// <summary>
        /// Whether canary channel is available.
        /// </summary>
        public bool canaryChannelAvailability
        {
            get => GetSettings(nameof(canaryChannelAvailability), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(canaryChannelAvailability)] = value;
            }
        }

        /// <summary>
        /// Whether local progressive loading is enabled.
        /// </summary>
        public bool localProgressiveLoad
        {
            get => GetSettings(nameof(localProgressiveLoad), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(localProgressiveLoad)] = value;
            }
        }

        /// <summary>
        /// Update source channel.
        /// </summary>
        public UpdateSource UpdateSource
        {
            get => GetSettings(nameof(UpdateSource), UpdateSource.Release);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UpdateSource)] = (int)value;
            }
        }
    }
}
