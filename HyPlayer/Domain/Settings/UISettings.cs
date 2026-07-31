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
            var backgroundType = value switch
            {
                4 => BackgroundType.Isolation,
                _ when Enum.IsDefined(typeof(BackgroundType), value) => (BackgroundType)value,
                _ => BackgroundType.Isolation
            };

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
}
