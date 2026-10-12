using System;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.UI.Playback.PlayBar;

namespace HyPlayer.Domain.Settings;

/// <summary>
///     Settings related to UI appearance, themes, animations, and visual effects.
/// </summary>
public partial class UISettings : SettingsBase
{
    protected override string SectionName => "ui";

    /// <summary>
    ///     Theme request (Auto, Light, Dark).
    /// </summary>
    public ThemeRequest ThemeRequest
    {
        get => GetSettings(nameof(ThemeRequest), ThemeRequest.Auto);
        set => SetSettings(nameof(ThemeRequest), (int)value);
    }

    /// <summary>
    ///     Whether expand animation is enabled.
    /// </summary>
    public bool ExpandAnimation
    {
        get => GetSettings(nameof(ExpandAnimation), true);
        set => SetSettings(nameof(ExpandAnimation), value ? "true" : "false");
    }

    /// <summary>
    ///     Whether to disable image loading.
    /// </summary>
    public bool NoImage
    {
        get => GetSettings(nameof(NoImage), false);
        set => SetSettings(nameof(NoImage), value);
    }

    /// <summary>
    ///     Lyric alignment setting.
    /// </summary>
    public LyricAlignment LyricAlignment
    {
        get => GetSettings(nameof(LyricAlignment), LyricAlignment.Left);
        set => SetSettings(nameof(LyricAlignment), (int)value);
    }

    /// <summary>
    ///     Lyric font size override (0 = default).
    /// </summary>
    public int LyricSize
    {
        get => GetSettings(nameof(LyricSize), 0);
        set => SetSettings(nameof(LyricSize), value);
    }

    /// <summary>
    ///     Lyric color mode.
    /// </summary>
    public LyricColor LyricColor
    {
        get => GetSettings(nameof(LyricColor), LyricColor.Auto);
        set => SetSettings(nameof(LyricColor), (int)value);
    }

    /// <summary>
    ///     Color generator type for theming.
    /// </summary>
    public ColorGeneratorType ColorGeneratorType
    {
        get => GetSettings(nameof(ColorGeneratorType), ColorGeneratorType.Auto);
        set => SetSettings(nameof(ColorGeneratorType), (int)value);
    }

    /// <summary>
    ///     Expanded player background type.
    /// </summary>
    public BackgroundType ExpandedPlayerBackgroundType
    {
        get
        {
            var value = GetSettings(nameof(ExpandedPlayerBackgroundType), (int)BackgroundType.Isolation);
            var backgroundType = Enum.IsDefined(typeof(BackgroundType), value)
                ? (BackgroundType)value
                : BackgroundType.Isolation;

            return backgroundType;
        }
        set => SetSettings(nameof(ExpandedPlayerBackgroundType), (int)value);
    }

    /// <summary>
    ///     Whether album cover rotates during playback.
    /// </summary>
    public bool AlbumRotate
    {
        get => GetSettings(nameof(AlbumRotate), false);
        set
        {
            if (SetSettings(nameof(AlbumRotate), value) && value)
                AlbumRound = true;
        }
    }

    /// <summary>
    ///     Whether album cover is displayed as round.
    /// </summary>
    public bool AlbumRound
    {
        get => GetSettings(nameof(AlbumRound), false);
        set
        {
            if (SetSettings(nameof(AlbumRound), value) && !value)
                AlbumRotate = false;
        }
    }

    /// <summary>
    ///     Album border length.
    /// </summary>
    public int AlbumBorderLength
    {
        get => GetSettings(nameof(AlbumBorderLength), 0);
        set => SetSettings(nameof(AlbumBorderLength), value);
    }

    /// <summary>
    ///     Whether expanded player uses acrylic.
    /// </summary>
    public bool ExpandedUseAcrylic
    {
        get => GetSettings(nameof(ExpandedUseAcrylic), true);
        set => SetSettings(nameof(ExpandedUseAcrylic), value);
    }

    /// <summary>
    ///     Whether expanded album has breath animation.
    /// </summary>
    public bool ExpandAlbumBreath
    {
        get => GetSettings(nameof(ExpandAlbumBreath), false);
        set => SetSettings(nameof(ExpandAlbumBreath), value);
    }

    /// <summary>
    ///     Whether expanded player shows full cover.
    /// </summary>
    public bool ExpandedPlayerFullCover
    {
        get => GetSettings(nameof(ExpandedPlayerFullCover), false);
        set => SetSettings(nameof(ExpandedPlayerFullCover), value);
    }

    /// <summary>
    ///     Whether the expanded player displays Win2D rendering diagnostics.
    /// </summary>
    public bool ExpandedPlayerDebugMode
    {
        get => GetSettings(nameof(ExpandedPlayerDebugMode), false);
        set => SetSettings(nameof(ExpandedPlayerDebugMode), value);
    }

    /// <summary>
    ///     Expanded cover shadow depth.
    /// </summary>
    public int ExpandedCoverShadowDepth
    {
        get => GetSettings(nameof(ExpandedCoverShadowDepth), 4);
        set => SetSettings(nameof(ExpandedCoverShadowDepth), value);
    }

    /// <summary>
    ///     Whether compact player page blur is enabled.
    /// </summary>
    public bool CompactPlayerPageBlurStatus
    {
        get => GetSettings(nameof(CompactPlayerPageBlurStatus), false);
        set => SetSettings(nameof(CompactPlayerPageBlurStatus), value);
    }

    /// <summary>
    ///     Whether not-clear mode is enabled.
    /// </summary>
    public bool NotClearMode
    {
        get => GetSettings(nameof(NotClearMode), true);
        set => SetSettings(nameof(NotClearMode), value);
    }

    /// <summary>
    ///     Whether playbar auto-hides.
    /// </summary>
    public bool AutoHidePlaybar
    {
        get => GetSettings(nameof(AutoHidePlaybar), false);
        set => SetSettings(nameof(AutoHidePlaybar), value);
    }

    /// <summary>
    ///     Auto-hide playbar timeout in seconds.
    /// </summary>
    public int AutoHidePlaybarTime
    {
        get => GetSettings(nameof(AutoHidePlaybarTime), 3);
        set
        {
            if (SetSettings(nameof(AutoHidePlaybarTime), value))
                Ioc.Default.GetRequiredService<IPlayBarAutoHideService>().SecondCounter = 0;
        }
    }

    /// <summary>
    ///     Whether playbar has margin.
    /// </summary>
    public bool PlayBarMargin
    {
        get => GetSettings(nameof(PlayBarMargin), true);
        set => SetSettings(nameof(PlayBarMargin), value);
    }

    /// <summary>
    ///     Whether UI sounds are enabled.
    /// </summary>
    public bool UISound
    {
        get => GetSettings(nameof(UISound), false);
        set => SetSettings(nameof(UISound), value);
    }

    /// <summary>
    ///     Whether to display shuffled list.
    /// </summary>
    public bool DisplayShuffledList
    {
        get => GetSettings(nameof(DisplayShuffledList), true);
        set => SetSettings(nameof(DisplayShuffledList), value);
    }

    /// <summary>
    ///     Whether to display maintenance info.
    /// </summary>
    public bool DisplayMaintain
    {
        get => GetSettings(nameof(DisplayMaintain), false);
        set => SetSettings(nameof(DisplayMaintain), value);
    }

    /// <summary>
    ///     Whether touch gesture actions are enabled.
    /// </summary>
    public bool EnableTouchGestureAction
    {
        get => GetSettings(nameof(EnableTouchGestureAction), false);
        set => SetSettings(nameof(EnableTouchGestureAction), value);
    }

    /// <summary>
    ///     Gesture mode.
    /// </summary>
    public GestureMode GestureMode
    {
        get => GetSettings(nameof(GestureMode), GestureMode.Basic);
        set => SetSettings(nameof(GestureMode), (int)value);
    }

    public bool KeyboardShortcutsEnabled
    {
        get => GetSettings(nameof(KeyboardShortcutsEnabled), true);
        set => SetSettings(nameof(KeyboardShortcutsEnabled), value);
    }

    public string KeyboardShortcutBindings
    {
        get => GetSettings(nameof(KeyboardShortcutBindings), string.Empty);
        set => SetSettings(nameof(KeyboardShortcutBindings), value);
    }

    public string SidebarLayout
    {
        get => GetSettings(nameof(SidebarLayout), string.Empty);
        set => SetSettings(nameof(SidebarLayout), value);
    }

    public string CustomUiFontFamily
    {
        get => GetSettings(nameof(CustomUiFontFamily), string.Empty);
        set => SetSettings(nameof(CustomUiFontFamily), value ?? string.Empty);
    }

    public string BackgroundImagePath
    {
        get => GetSettings(nameof(BackgroundImagePath), string.Empty);
        set
        {
            if (SetSettings(nameof(BackgroundImagePath), value ?? string.Empty))
                OnPropertyChanged(nameof(HasCustomBackground));
        }
    }

    public bool HasCustomBackground => !string.IsNullOrEmpty(BackgroundImagePath);

    public double BackgroundImageOpacity
    {
        get => GetSettings(nameof(BackgroundImageOpacity), 35d);
        set => SetSettings(nameof(BackgroundImageOpacity), value);
    }

    public double BackgroundImageBlur
    {
        get => GetSettings(nameof(BackgroundImageBlur), 0d);
        set => SetSettings(nameof(BackgroundImageBlur), value);
    }

    public int BackgroundImageStretch
    {
        get => GetSettings(nameof(BackgroundImageStretch), 0);
        set
        {
            if (value >= 0)
                SetSettings(nameof(BackgroundImageStretch), value);
        }
    }

    /// <summary>
    ///     Whether animation adapts to BPM.
    /// </summary>
    public bool AnimationAdaptBPM
    {
        get => GetSettings(nameof(AnimationAdaptBPM), false);
        set => SetSettings(nameof(AnimationAdaptBPM), value);
    }

    /// <summary>
    ///     Whether gentle BPM animation is enabled.
    /// </summary>
    public bool GentleBPMAnimation
    {
        get => GetSettings(nameof(GentleBPMAnimation), false);
        set => SetSettings(nameof(GentleBPMAnimation), value);
    }

    /// <summary>
    ///     Whether popup notifications are disabled.
    /// </summary>
    public bool DisablePopUp
    {
        get => GetSettings(nameof(DisablePopUp), false);
        set => SetSettings(nameof(DisablePopUp), value);
    }

    /// <summary>
    ///     Whether canary channel is available.
    /// </summary>
    public bool CanaryChannelAvailability
    {
        get => GetSettings(nameof(CanaryChannelAvailability), false);
        set => SetSettings(nameof(CanaryChannelAvailability), value);
    }

    /// <summary>
    ///     Update source channel.
    /// </summary>
    public UpdateSource UpdateSource
    {
        get => GetSettings(nameof(UpdateSource), UpdateSource.Release);
        set => SetSettings(nameof(UpdateSource), (int)value);
    }

    public bool EnableTile
    {
        get => GetSettings(nameof(EnableTile), false);
        set => SetSettings(nameof(EnableTile), value);
    }

    public bool EnableTileBackground
    {
        get => GetSettings(nameof(EnableTileBackground), false);
        set => SetSettings(nameof(EnableTileBackground), value);
    }

    public static readonly int[] BackgroundReleasePresets = [30, 60, 300, 600, 0];

    public int BackgroundReleaseSeconds
    {
        get => GetSettings(nameof(BackgroundReleaseSeconds), 60);
        set
        {
            if (SetSettings(nameof(BackgroundReleaseSeconds), value))
                OnPropertyChanged(nameof(BackgroundReleaseIndex));
        }
    }

    public int BackgroundReleaseIndex
    {
        get => Math.Max(0, Array.IndexOf(BackgroundReleasePresets, BackgroundReleaseSeconds));
        set
        {
            if (value >= 0 && value < BackgroundReleasePresets.Length)
                BackgroundReleaseSeconds = BackgroundReleasePresets[value];
        }
    }

    public static readonly int[] MemoryLimitPresets = [-1, 512, 1024, 2048, 3072, 4096, 0];

    public int MemoryLimitMB
    {
        get => GetSettings(nameof(MemoryLimitMB), -1);
        set
        {
            if (SetSettings(nameof(MemoryLimitMB), value))
                OnPropertyChanged(nameof(MemoryLimitIndex));
        }
    }

    public int MemoryLimitIndex
    {
        get => Math.Max(0, Array.IndexOf(MemoryLimitPresets, MemoryLimitMB));
        set
        {
            if (value >= 0 && value < MemoryLimitPresets.Length)
                MemoryLimitMB = MemoryLimitPresets[value];
        }
    }

    public ExpandedControlStyle ExpandedControlStyle
    {
        get => GetSettings(nameof(ExpandedControlStyle), ExpandedControlStyle.Apple);
        set => SetSettings(nameof(ExpandedControlStyle), (int)value);
    }

    public bool ExpandedAutoCollapsePlaybar
    {
        get => GetSettings(nameof(ExpandedAutoCollapsePlaybar), true);
        set => SetSettings(nameof(ExpandedAutoCollapsePlaybar), value);
    }

    public bool HidePlaybarOnHome
    {
        get => GetSettings(nameof(HidePlaybarOnHome), false);
        set => SetSettings(nameof(HidePlaybarOnHome), value);
    }

    public bool PlaybarProgressiveBlur
    {
        get => GetSettings(nameof(PlaybarProgressiveBlur), true);
        set => SetSettings(nameof(PlaybarProgressiveBlur), value);
    }

    public bool XboxHidePointer
    {
        get => GetSettings(nameof(XboxHidePointer), false);
        set => SetSettings(nameof(XboxHidePointer), value);
    }

    public bool IsOldThemeEnabled
    {
        get => GetSettings(nameof(IsOldThemeEnabled), false);
        set => SetSettings(nameof(IsOldThemeEnabled), value);
    }

    public bool ListHeaderAcrylicBlur
    {
        get => GetSettings(nameof(ListHeaderAcrylicBlur), true);
        set => SetSettings(nameof(ListHeaderAcrylicBlur), value);
    }

    public bool ItemOfListBackgroundAcrylicBlur
    {
        get => GetSettings(nameof(ItemOfListBackgroundAcrylicBlur), false);
        set => SetSettings(nameof(ItemOfListBackgroundAcrylicBlur), value);
    }

    public bool PlaybarBackgroundAcrylic
    {
        get => GetSettings(nameof(PlaybarBackgroundAcrylic), false);
        set => SetSettings(nameof(PlaybarBackgroundAcrylic), value);
    }

    public bool PlaybarBackgroundBreath
    {
        get => GetSettings(nameof(PlaybarBackgroundBreath), false);
        set => SetSettings(nameof(PlaybarBackgroundBreath), value);
    }

    public bool PlaybarBackgroundElay
    {
        get => GetSettings(nameof(PlaybarBackgroundElay), false);
        set => SetSettings(nameof(PlaybarBackgroundElay), value);
    }

    public bool PlaybarButtonsTransparent
    {
        get => GetSettings(nameof(PlaybarButtonsTransparent), true);
        set => SetSettings(nameof(PlaybarButtonsTransparent), value);
    }

    public bool PlayButtonAccentColor
    {
        get => GetSettings(nameof(PlayButtonAccentColor), false);
        set => SetSettings(nameof(PlayButtonAccentColor), value);
    }

    public bool AcrylicBackgroundStatus
    {
        get => GetSettings(nameof(AcrylicBackgroundStatus), false);
        set => SetSettings(nameof(AcrylicBackgroundStatus), value);
    }

    public bool CustomAcrylic
    {
        get => GetSettings(nameof(CustomAcrylic), false);
        set => SetSettings(nameof(CustomAcrylic), value);
    }

    public double CustomTintOpacity
    {
        get => CustomAcrylic ? GetSettings(nameof(CustomTintOpacity), 3d) : 0d;
        set => SetSettings(nameof(CustomTintOpacity), value);
    }

    public double CustomTintLuminosityOpacity
    {
        get => CustomAcrylic ? GetSettings(nameof(CustomTintLuminosityOpacity), 3d) : 0d;
        set => SetSettings(nameof(CustomTintLuminosityOpacity), value);
    }

    public static bool AcrylicAvailability =>
        new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled &&
        Windows.UI.Composition.CompositionCapabilities.GetForCurrentView().AreEffectsFast();
}
