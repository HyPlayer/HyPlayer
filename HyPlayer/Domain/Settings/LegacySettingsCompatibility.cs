using System;
using Windows.UI;

namespace HyPlayer.Domain.Settings;

// The settings page still uses the historical Setting facade.  These aliases
// keep that page source-compatible with the split settings objects.
public partial class PlaybackSettings
{
    public string audioRate { get => AudioRate; set => AudioRate = value; }

    private bool _abRepeatStatus;
    private TimeSpan _abStartPoint;
    private TimeSpan _abEndPoint;
    public bool ABRepeatStatus { get => _abRepeatStatus; set => _abRepeatStatus = value; }
    public TimeSpan ABStartPoint { get => _abStartPoint; set => _abStartPoint = value; }
    public string ABStartPointFriendlyValue => _abStartPoint.ToString();
    public TimeSpan ABEndPoint { get => _abEndPoint; set => _abEndPoint = value; }
    public string ABEndPointFriendlyValue => _abEndPoint.ToString();
    public bool enableCache { get => EnableCache; set => EnableCache = value; }
    public string cacheDir { get => CacheDirectory; set => CacheDirectory = value; }
}

public partial class UISettings
{
    public ThemeRequest themeRequest { get => ThemeRequest; set => ThemeRequest = value; }
    public bool expandAnimation { get => ExpandAnimation; set => ExpandAnimation = value; }
    public bool noImage { get => NoImage; set => NoImage = value; }
    public LyricAlignment lyricAlignment { get => LyricAlignment; set => LyricAlignment = value; }
    public int lyricSize { get => LyricSize; set => LyricSize = value; }
    public LyricColor lyricColor { get => LyricColor; set => LyricColor = value; }
    public BackgroundType expandedPlayerBackgroundType { get => ExpandedPlayerBackgroundType; set => ExpandedPlayerBackgroundType = value; }
    public bool albumRotate { get => AlbumRotate; set => AlbumRotate = value; }
    public bool albumRound { get => AlbumRound; set => AlbumRound = value; }
    public int albumBorderLength { get => AlbumBorderLength; set => AlbumBorderLength = value; }
    public bool expandedUseAcrylic { get => ExpandedUseAcrylic; set => ExpandedUseAcrylic = value; }
    public bool expandAlbumBreath { get => ExpandAlbumBreath; set => ExpandAlbumBreath = value; }
    public bool expandedPlayerFullCover { get => ExpandedPlayerFullCover; set => ExpandedPlayerFullCover = value; }
    public int expandedCoverShadowDepth { get => ExpandedCoverShadowDepth; set => ExpandedCoverShadowDepth = value; }
    public bool notClearMode { get => NotClearMode; set => NotClearMode = value; }
    public bool playBarMargin { get => PlayBarMargin; set => PlayBarMargin = value; }
    public bool uiSound { get => UISound; set => UISound = value; }
    public bool displayShuffledList { get => DisplayShuffledList; set => DisplayShuffledList = value; }
    public bool displayMaintain { get => DisplayMaintain; set => DisplayMaintain = value; }
    public bool enableTouchGestureAction { get => EnableTouchGestureAction; set => EnableTouchGestureAction = value; }
    public GestureMode gestureMode { get => GestureMode; set => GestureMode = value; }
    public bool animationAdaptBPM { get => AnimationAdaptBPM; set => AnimationAdaptBPM = value; }
    public bool gentleBPMAnimation { get => GentleBPMAnimation; set => GentleBPMAnimation = value; }
    public bool canaryChannelAvailability { get => CanaryChannelAvailability; set => CanaryChannelAvailability = value; }
    public bool localProgressiveLoad
    {
        get => GetSettings(nameof(localProgressiveLoad), false);
        set => SetSettings(nameof(localProgressiveLoad), value);
    }
}

public partial class ApiSettings
{
    public bool enableApiCache { get => EnableApiCache; set => EnableApiCache = value; }
    public bool songUrlLazyGet { get => SongUrlLazyGet; set => SongUrlLazyGet = value; }
    public bool greedlyLoadPlayContainerItems { get => GreedilyLoadPlayContainerItems; set => GreedilyLoadPlayContainerItems = value; }
    public bool jumpVipSongPlaying { get => JumpVipSongPlaying; set => JumpVipSongPlaying = value; }
    public bool jumpVipSongDownloading { get => JumpVipSongDownloading; set => JumpVipSongDownloading = value; }
}

public partial class LyricSettings
{
    public bool showComposerInLyric { get => ShowComposerInLyric; set => ShowComposerInLyric = value; }
    public bool downloadLyric { get => DownloadLyric; set => DownloadLyric = value; }
    public bool downloadTranslation { get => DownloadTranslation; set => DownloadTranslation = value; }
    public bool lyricDropshadow { get => LyricDropShadow; set => LyricDropShadow = value; }
    public bool lyricCacheRenderTarget { get => LyricCacheRenderTarget; set => LyricCacheRenderTarget = value; }
    public int lyricScaleSize { get => LyricScaleSize; set => LyricScaleSize = value; }
    public int lyricLineSpacing { get => LyricLineSpacing; set => LyricLineSpacing = value; }
    public int translationSize { get => TranslationSize; set => TranslationSize = value; }
    public int romajiSize { get => RomajiSize; set => RomajiSize = value; }
    public int sublineLyricSize { get => SublineLyricSize; set => SublineLyricSize = value; }
    public int sublineTranslationSize { get => SublineTranslationSize; set => SublineTranslationSize = value; }
    public int sublineRomajiSize { get => SublineRomajiSize; set => SublineRomajiSize = value; }
    public int lyricPaddingTopRatio { get => LyricPaddingTopRatio; set => LyricPaddingTopRatio = value; }
    public int lyricFadingRatio { get => LyricFadingRatio; set => LyricFadingRatio = value; }
    public bool hotlyricOnStartup { get => HotLyricOnStartup; set => HotLyricOnStartup = value; }
    public bool enableAmllTtmlDb { get => EnableAmllTtmlDb; set => EnableAmllTtmlDb = value; }
    public string amllTtmlMirrorUrl { get => AmllTtmlMirrorUrl; set => AmllTtmlMirrorUrl = value; }
    public bool lyricRenderFocusHighlighting { get => LyricRenderFocusHighlighting; set => LyricRenderFocusHighlighting = value; }
    public int lyricRenderWidthRatio { get => LyricRenderWidthRatio; set => LyricRenderWidthRatio = value; }
    public bool lyricRenderTransliterationScanning { get => LyricRenderTransliterationScanning; set => LyricRenderTransliterationScanning = value; }
    public bool lyricRenderSimpleLineScanning { get => LyricRenderSimpleLineScanning; set => LyricRenderSimpleLineScanning = value; }
    public LyricScanStyle lyricRenderScanStyle { get => LyricRenderScanStyle; set => LyricRenderScanStyle = value; }
    public bool lyricRenderScaleWhenFocusing { get => LyricRenderScaleWhenFocusing; set => LyricRenderScaleWhenFocusing = value; }
    public bool lyricRenderTransform3D { get => LyricRenderTransform3D; set => LyricRenderTransform3D = value; }
    public bool lyricRenderBlur { get => LyricRenderBlur; set => LyricRenderBlur = value; }
    public bool lyricRenderFade { get => LyricRenderFade; set => LyricRenderFade = value; }
    public Color? pureLyricIdleColor { get => PureLyricIdleColor; set => PureLyricIdleColor = value; }
    public Color? pureLyricFocusingColor { get => PureLyricFocusingColor; set => PureLyricFocusingColor = value; }
    public Color? karaokLyricFocusingColor { get => KaraokeLyricFocusingColor; set => KaraokeLyricFocusingColor = value; }
}

public partial class LastFMSettings
{
    public bool useAiDj { get => UseAiDj; set => UseAiDj = value; }
}
