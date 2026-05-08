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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to force memory garbage collection.
        /// </summary>
        public bool forceMemoryGarbage
        {
            get => GetSettings(nameof(forceMemoryGarbage), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(forceMemoryGarbage)] = value;
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether old theme is enabled.
        /// </summary>
        public bool IsOldThemeEnabled
        {
            get => GetSettings(nameof(IsOldThemeEnabled), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsOldThemeEnabled)] = value;
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether custom acrylic is enabled.
        /// </summary>
        public bool CustomAcrylic
        {
            get => GetSettings(nameof(CustomAcrylic), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(CustomAcrylic)] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(acrylicBackgroundStatus));
            }
        }

        /// <summary>
        /// Custom tint opacity for acrylic.
        /// </summary>
        public double CustomTintOpacity
        {
            get
            {
                try
                {
                    if (CustomAcrylic)
                    {
                        return GetSettings(nameof(CustomTintOpacity), 3d);
                    }
                    else
                    {
                        return 0d;
                    }
                }
                catch
                {
                    return 3d;
                }
            }
            set => ApplicationData.Current.LocalSettings.Values[nameof(CustomTintOpacity)] = value;
        }

        /// <summary>
        /// Custom tint luminosity opacity for acrylic.
        /// </summary>
        public double CustomTintLuminosityOpacity
        {
            get
            {
                try
                {
                    if (CustomAcrylic)
                    {
                        return GetSettings(nameof(CustomTintLuminosityOpacity), 3d);
                    }
                    else
                    {
                        return 0d;
                    }
                }
                catch
                {
                    return 3d;
                }
            }
            set => ApplicationData.Current.LocalSettings.Values[nameof(CustomTintLuminosityOpacity)] = value;
        }

        /// <summary>
        /// Whether acrylic background is enabled.
        /// </summary>
        public bool acrylicBackgroundStatus
        {
            get => GetSettings(nameof(acrylicBackgroundStatus), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(acrylicBackgroundStatus)] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(playbarBackgroundAcrylic));
            }
        }

        /// <summary>
        /// Whether acrylic effects are available on this system.
        /// </summary>
        public static bool acrylicAvailabiliity => new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled && Windows.UI.Composition.CompositionCapabilities.GetForCurrentView().AreEffectsFast();

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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
        /// Whether playbar background has breath animation.
        /// </summary>
        public bool playbarBackgroundBreath
        {
            get => GetSettings(nameof(playbarBackgroundBreath), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundBreath)] = value;
        }

        /// <summary>
        /// Whether playbar background uses acrylic.
        /// </summary>
        public bool playbarBackgroundAcrylic
        {
            get => GetSettings(nameof(playbarBackgroundAcrylic), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundAcrylic)] = value;
                OnPropertyChanged();
            }
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
        /// Whether list header uses acrylic blur.
        /// </summary>
        public bool listHeaderAcrylicBlur
        {
            get => GetSettings(nameof(listHeaderAcrylicBlur), true);
            set => ApplicationData.Current.LocalSettings.Values[nameof(listHeaderAcrylicBlur)] = value;
        }

        /// <summary>
        /// Whether list item background uses acrylic blur.
        /// </summary>
        public bool itemOfListBackgroundAcrylicBlur
        {
            get => GetSettings(nameof(itemOfListBackgroundAcrylicBlur), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(itemOfListBackgroundAcrylicBlur)] = value;
        }

        /// <summary>
        /// Whether playbar buttons are transparent.
        /// </summary>
        public bool playbarButtonsTransparent
        {
            get => GetSettings(nameof(playbarButtonsTransparent), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarButtonsTransparent)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether playbar background has elay effect.
        /// </summary>
        public bool playbarBackgroundElay
        {
            get => GetSettings(nameof(playbarBackgroundElay), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundElay)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether play button uses accent color.
        /// </summary>
        public bool playButtonAccentColor
        {
            get => GetSettings(nameof(playButtonAccentColor), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playButtonAccentColor)] = value;
                OnPropertyChanged();
            }
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether title bar immerse mode is enabled.
        /// </summary>
        public bool EnableTitleBarImmerse
        {
            get => GetSettings("enableTitleBarImmerse", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["enableTitleBarImmerse"] = value;
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to hide pointer on Xbox.
        /// </summary>
        public bool xboxHidePointer
        {
            get => GetSettings(nameof(xboxHidePointer), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(xboxHidePointer)] = value;
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
                OnPropertyChanged();
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
                OnPropertyChanged();
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
        /// Whether tile is enabled.
        /// </summary>
        public bool enableTile
        {
            get => GetSettings(nameof(enableTile), System.Environment.OSVersion.Version.Build < 22000);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableTile)] = value;
                if (!value)
                {
                    tileBackgroundAvailability = false;
                    saveTileBackgroundToLocalFolder = false;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether tile background is available.
        /// </summary>
        public bool tileBackgroundAvailability
        {
            get => GetSettings(nameof(tileBackgroundAvailability), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(tileBackgroundAvailability)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to save tile background to local folder.
        /// </summary>
        public bool saveTileBackgroundToLocalFolder
        {
            get => GetSettings(nameof(saveTileBackgroundToLocalFolder), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(saveTileBackgroundToLocalFolder)] = value;
                OnPropertyChanged();
            }
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to use high quality cover in SMTC.
        /// </summary>
        public bool highQualityCoverInSMTC
        {
            get => GetSettings(nameof(highQualityCoverInSMTC), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(highQualityCoverInSMTC)] = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Whether to use taglib for picture extraction.
        /// </summary>
        public bool useTaglibPicture
        {
            get => GetSettings(nameof(useTaglibPicture), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(useTaglibPicture)] = value;
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }
    }
}
