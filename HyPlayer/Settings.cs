using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.Classes.Settings;
using HyPlayer.NeteaseApi;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using LiteFM.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Windows.Storage;
using Windows.UI;
using HyPlayerUISettings = HyPlayer.Classes.Settings.UISettings;

namespace HyPlayer
{
    public partial class Setting : INotifyPropertyChanged
    {
        /// <summary>
        /// Playback-related settings (volume, crossfade, audio device, etc.).
        /// </summary>
        public PlaybackSettings Playback { get; } = new PlaybackSettings();

        /// <summary>
        /// UI appearance settings (theme, acrylic, animations, etc.).
        /// </summary>
        public HyPlayerUISettings UI { get; } = new HyPlayerUISettings();

        /// <summary>
        /// API and network settings (proxy, HTTP, caching, etc.).
        /// </summary>
        public ApiSettings Api { get; } = new ApiSettings();

        /// <summary>
        /// Lyric display and rendering settings.
        /// </summary>
        public LyricSettings Lyric { get; } = new LyricSettings();

        /// <summary>
        /// Last.FM integration settings.
        /// </summary>
        public LastFMSettings LastFM { get; } = new LastFMSettings();

        // ===================================================================
        // Pass-through delegates — preserves the original public API.
        // All existing consumers continue to work without changes.
        // ===================================================================

        // --- PlaybackSettings delegates ---

        public int Volume { get => Playback.Volume; set => Playback.Volume = value; }
        public string audioRate { get => Playback.audioRate; set => Playback.audioRate = value; }
        public bool CrossFade { get => Playback.CrossFade; set => Playback.CrossFade = value; }
        public double CrossFadeTime { get => Playback.CrossFadeTime; set => Playback.CrossFadeTime = value; }
        public bool EnableAudioGain { get => Playback.EnableAudioGain; set => Playback.EnableAudioGain = value; }
        public bool ABRepeatStatus { get => Playback.ABRepeatStatus; set => Playback.ABRepeatStatus = value; }
        public TimeSpan ABStartPoint { get => Playback.ABStartPoint; set => Playback.ABStartPoint = value; }
        public string ABStartPointFriendlyValue => Playback.ABStartPointFriendlyValue;
        public TimeSpan ABEndPoint { get => Playback.ABEndPoint; set => Playback.ABEndPoint = value; }
        public string ABEndPointFriendlyValue => Playback.ABEndPointFriendlyValue;
        public bool enableCache { get => Playback.enableCache; set => Playback.enableCache = value; }
        public string cacheDir { get => Playback.cacheDir; set => Playback.cacheDir = value; }
        public string AudioRenderDevice { get => Playback.AudioRenderDevice; set => Playback.AudioRenderDevice = value; }
        public bool EnableFFT { get => Playback.EnableFFT; set => Playback.EnableFFT = value; }
        public bool shuffleNoRepeating { get => Playback.shuffleNoRepeating; set => Playback.shuffleNoRepeating = value; }

        // --- UISettings delegates ---

        public ThemeRequest themeRequest { get => UI.themeRequest; set => UI.themeRequest = value; }
        public bool expandAnimation { get => UI.expandAnimation; set => UI.expandAnimation = value; }
        public bool forceMemoryGarbage { get => UI.forceMemoryGarbage; set => UI.forceMemoryGarbage = value; }
        public bool noImage { get => UI.noImage; set => UI.noImage = value; }
        public LyricAlignment lyricAlignment { get => UI.lyricAlignment; set => UI.lyricAlignment = value; }
        public int lyricSize { get => UI.lyricSize; set => UI.lyricSize = value; }
        public LyricColor lyricColor { get => UI.lyricColor; set => UI.lyricColor = value; }
        public ColorGeneratorType ColorGeneratorType { get => UI.ColorGeneratorType; set => UI.ColorGeneratorType = value; }
        public bool IsOldThemeEnabled { get => UI.IsOldThemeEnabled; set => UI.IsOldThemeEnabled = value; }
        public BackgroundType expandedPlayerBackgroundType { get => UI.expandedPlayerBackgroundType; set => UI.expandedPlayerBackgroundType = value; }
        public bool CustomAcrylic { get => UI.CustomAcrylic; set => UI.CustomAcrylic = value; }
        public double CustomTintOpacity { get => UI.CustomTintOpacity; set => UI.CustomTintOpacity = value; }
        public double CustomTintLuminosityOpacity { get => UI.CustomTintLuminosityOpacity; set => UI.CustomTintLuminosityOpacity = value; }
        public bool acrylicBackgroundStatus { get => UI.acrylicBackgroundStatus; set => UI.acrylicBackgroundStatus = value; }
        public static bool acrylicAvailabiliity => HyPlayerUISettings.acrylicAvailabiliity;
        public bool albumRotate { get => UI.albumRotate; set => UI.albumRotate = value; }
        public bool albumRound { get => UI.albumRound; set => UI.albumRound = value; }
        public int albumBorderLength { get => UI.albumBorderLength; set => UI.albumBorderLength = value; }
        public bool expandedUseAcrylic { get => UI.expandedUseAcrylic; set => UI.expandedUseAcrylic = value; }
        public bool playbarBackgroundBreath { get => UI.playbarBackgroundBreath; set => UI.playbarBackgroundBreath = value; }
        public bool playbarBackgroundAcrylic { get => UI.playbarBackgroundAcrylic; set => UI.playbarBackgroundAcrylic = value; }
        public bool expandAlbumBreath { get => UI.expandAlbumBreath; set => UI.expandAlbumBreath = value; }
        public bool listHeaderAcrylicBlur { get => UI.listHeaderAcrylicBlur; set => UI.listHeaderAcrylicBlur = value; }
        public bool itemOfListBackgroundAcrylicBlur { get => UI.itemOfListBackgroundAcrylicBlur; set => UI.itemOfListBackgroundAcrylicBlur = value; }
        public bool playbarButtonsTransparent { get => UI.playbarButtonsTransparent; set => UI.playbarButtonsTransparent = value; }
        public bool playbarBackgroundElay { get => UI.playbarBackgroundElay; set => UI.playbarBackgroundElay = value; }
        public bool playButtonAccentColor { get => UI.playButtonAccentColor; set => UI.playButtonAccentColor = value; }
        public bool expandedPlayerFullCover { get => UI.expandedPlayerFullCover; set => UI.expandedPlayerFullCover = value; }
        public int expandedCoverShadowDepth { get => UI.expandedCoverShadowDepth; set => UI.expandedCoverShadowDepth = value; }
        public bool EnableTitleBarImmerse { get => UI.EnableTitleBarImmerse; set => UI.EnableTitleBarImmerse = value; }
        public bool CompactPlayerPageBlurStatus { get => UI.CompactPlayerPageBlurStatus; set => UI.CompactPlayerPageBlurStatus = value; }
        public bool notClearMode { get => UI.notClearMode; set => UI.notClearMode = value; }
        public bool AutoHidePlaybar { get => UI.AutoHidePlaybar; set => UI.AutoHidePlaybar = value; }
        public int AutoHidePlaybarTime { get => UI.AutoHidePlaybarTime; set => UI.AutoHidePlaybarTime = value; }
        public bool playBarMargin { get => UI.playBarMargin; set => UI.playBarMargin = value; }
        public bool uiSound { get => UI.uiSound; set => UI.uiSound = value; }
        public bool displayShuffledList { get => UI.displayShuffledList; set => UI.displayShuffledList = value; }
        public bool displayMaintain { get => UI.displayMaintain; set => UI.displayMaintain = value; }
        public bool xboxHidePointer { get => UI.xboxHidePointer; set => UI.xboxHidePointer = value; }
        public bool enableTouchGestureAction { get => UI.enableTouchGestureAction; set => UI.enableTouchGestureAction = value; }
        public GestureMode gestureMode { get => UI.gestureMode; set => UI.gestureMode = value; }
        public bool animationAdaptBPM { get => UI.animationAdaptBPM; set => UI.animationAdaptBPM = value; }
        public bool gentleBPMAnimation { get => UI.gentleBPMAnimation; set => UI.gentleBPMAnimation = value; }
        public bool DisablePopUp { get => UI.DisablePopUp; set => UI.DisablePopUp = value; }
        public bool enableTile { get => UI.enableTile; set => UI.enableTile = value; }
        public bool tileBackgroundAvailability { get => UI.tileBackgroundAvailability; set => UI.tileBackgroundAvailability = value; }
        public bool saveTileBackgroundToLocalFolder { get => UI.saveTileBackgroundToLocalFolder; set => UI.saveTileBackgroundToLocalFolder = value; }
        public bool canaryChannelAvailability { get => UI.canaryChannelAvailability; set => UI.canaryChannelAvailability = value; }
        public bool localProgressiveLoad { get => UI.localProgressiveLoad; set => UI.localProgressiveLoad = value; }
        public bool highQualityCoverInSMTC { get => UI.highQualityCoverInSMTC; set => UI.highQualityCoverInSMTC = value; }
        public bool useTaglibPicture { get => UI.useTaglibPicture; set => UI.useTaglibPicture = value; }
        public UpdateSource UpdateSource { get => UI.UpdateSource; set => UI.UpdateSource = value; }

        // --- ApiSettings delegates ---

        public bool EnableProxy { get => Api.EnableProxy; set => Api.EnableProxy = value; }
        public AdditionalParameters ApiAdditionalParameters { get => Api.ApiAdditionalParameters; set => Api.ApiAdditionalParameters = value; }
        public bool UseHttp { get => Api.UseHttp; set => Api.UseHttp = value; }
        public bool UseHttpWhenGettingSongs { get => Api.UseHttpWhenGettingSongs; set => Api.UseHttpWhenGettingSongs = value; }
        public bool EnableCheckTokenApi { get => Api.EnableCheckTokenApi; set => Api.EnableCheckTokenApi = value; }
        public bool enableApiCache { get => Api.enableApiCache; set => Api.enableApiCache = value; }
        public bool songUrlLazyGet { get => Api.songUrlLazyGet; set => Api.songUrlLazyGet = value; }
        public bool greedlyLoadPlayContainerItems { get => Api.greedlyLoadPlayContainerItems; set => Api.greedlyLoadPlayContainerItems = value; }
        public bool AutoAddGreedilyLoadedSongsToPlayList { get => Api.AutoAddGreedilyLoadedSongsToPlayList; set => Api.AutoAddGreedilyLoadedSongsToPlayList = value; }
        public bool jumpVipSongPlaying { get => Api.jumpVipSongPlaying; set => Api.jumpVipSongPlaying = value; }
        public bool jumpVipSongDownloading { get => Api.jumpVipSongDownloading; set => Api.jumpVipSongDownloading = value; }

        // --- LyricSettings delegates ---

        public RomajiSource LyricRomajiSource { get => Lyric.LyricRomajiSource; set => Lyric.LyricRomajiSource = value; }
        public bool highPreciseLyricTimer { get => Lyric.highPreciseLyricTimer; set => Lyric.highPreciseLyricTimer = value; }
        public bool karaokLyric { get => Lyric.karaokLyric; set => Lyric.karaokLyric = value; }
        public bool showComposerInLyric { get => Lyric.showComposerInLyric; set => Lyric.showComposerInLyric = value; }
        public bool downloadLyric { get => Lyric.downloadLyric; set => Lyric.downloadLyric = value; }
        public bool downloadTranslation { get => Lyric.downloadTranslation; set => Lyric.downloadTranslation = value; }
        public bool MigrateLyrics { get => Lyric.MigrateLyrics; set => Lyric.MigrateLyrics = value; }
        public bool OptimizeLyric { get => Lyric.OptimizeLyric; set => Lyric.OptimizeLyric = value; }
        public bool lyricDropshadow { get => Lyric.lyricDropshadow; set => Lyric.lyricDropshadow = value; }
        public bool lyricCacheRenderTarget { get => Lyric.lyricCacheRenderTarget; set => Lyric.lyricCacheRenderTarget = value; }
        public int lyricScaleSize { get => Lyric.lyricScaleSize; set => Lyric.lyricScaleSize = value; }
        public string lyricFontFamily { get => Lyric.lyricFontFamily; set => Lyric.lyricFontFamily = value; }
        public int lyricLineSpacing { get => Lyric.lyricLineSpacing; set => Lyric.lyricLineSpacing = value; }
        public int translationSize { get => Lyric.translationSize; set => Lyric.translationSize = value; }
        public int romajiSize { get => Lyric.romajiSize; set => Lyric.romajiSize = value; }
        public int lyricPaddingTopRatio { get => Lyric.lyricPaddingTopRatio; set => Lyric.lyricPaddingTopRatio = value; }
        public int lyricFadingRatio { get => Lyric.lyricFadingRatio; set => Lyric.lyricFadingRatio = value; }
        public bool hotlyricOnStartup { get => Lyric.hotlyricOnStartup; set => Lyric.hotlyricOnStartup = value; }
        public bool enableAmllTtmlDb { get => Lyric.enableAmllTtmlDb; set => Lyric.enableAmllTtmlDb = value; }
        public string amllTtmlMirrorUrl { get => Lyric.amllTtmlMirrorUrl; set => Lyric.amllTtmlMirrorUrl = value; }
        public bool lyricRenderFocusHighlighting { get => Lyric.lyricRenderFocusHighlighting; set => Lyric.lyricRenderFocusHighlighting = value; }
        public int lyricRenderWidthRatio { get => Lyric.lyricRenderWidthRatio; set => Lyric.lyricRenderWidthRatio = value; }
        public bool lyricRenderTransliterationScanning { get => Lyric.lyricRenderTransliterationScanning; set => Lyric.lyricRenderTransliterationScanning = value; }
        public bool lyricRenderSimpleLineScanning { get => Lyric.lyricRenderSimpleLineScanning; set => Lyric.lyricRenderSimpleLineScanning = value; }
        public bool lyricRenderScaleWhenFocusing { get => Lyric.lyricRenderScaleWhenFocusing; set => Lyric.lyricRenderScaleWhenFocusing = value; }
        public bool lyricRenderBlur { get => Lyric.lyricRenderBlur; set => Lyric.lyricRenderBlur = value; }
        public bool lyricRenderFade { get => Lyric.lyricRenderFade; set => Lyric.lyricRenderFade = value; }
        public RollingCalculator LineRollingCalculator { get => Lyric.LineRollingCalculator; set => Lyric.LineRollingCalculator = value; }
        public bool LyricRendererDebugMode { get => Lyric.LyricRendererDebugMode; set => Lyric.LyricRendererDebugMode = value; }
        public int LyricRendererFPS { get => Lyric.LyricRendererFPS; set => Lyric.LyricRendererFPS = value; }
#nullable enable
        public Color? pureLyricIdleColor { get => Lyric.pureLyricIdleColor; set => Lyric.pureLyricIdleColor = value; }
        public Color? pureLyricFocusingColor { get => Lyric.pureLyricFocusingColor; set => Lyric.pureLyricFocusingColor = value; }
        public Color? karaokLyricFocusingColor { get => Lyric.karaokLyricFocusingColor; set => Lyric.karaokLyricFocusingColor = value; }
#nullable restore
        public bool IsolationFullThrottle { get => Lyric.IsolationFullThrottle; set => Lyric.IsolationFullThrottle = value; }
        public double IsolationFPS { get => Lyric.IsolationFPS; set => Lyric.IsolationFPS = value; }
        public float IsolationScale { get => Lyric.IsolationScale; set => Lyric.IsolationScale = value; }
        public bool IsolationLightWave { get => Lyric.IsolationLightWave; set => Lyric.IsolationLightWave = value; }
        public bool ImpressionistLABSpace { get => Lyric.ImpressionistLABSpace; set => Lyric.ImpressionistLABSpace = value; }
        public bool ImpressionistIgnoreWhite { get => Lyric.ImpressionistIgnoreWhite; set => Lyric.ImpressionistIgnoreWhite = value; }
        public bool ImpressionistUseKMeansPP { get => Lyric.ImpressionistUseKMeansPP; set => Lyric.ImpressionistUseKMeansPP = value; }

        // --- LastFMSettings delegates ---

        public LastFMSession LastFMSession { get => LastFM.LastFMSession; set => LastFM.LastFMSession = value; }
        public bool UpdateLastFMNowPlaying { get => LastFM.UpdateLastFMNowPlaying; set => LastFM.UpdateLastFMNowPlaying = value; }
        public bool LastFMScrobble { get => LastFM.LastFMScrobble; set => LastFM.LastFMScrobble = value; }
        public bool useAiDj { get => LastFM.useAiDj; set => LastFM.useAiDj = value; }

        // ===================================================================
        // Properties that remain directly on Setting (not grouped)
        // ===================================================================

        public bool writedownloadFileInfo
        {
            get => GetSettings(nameof(writedownloadFileInfo), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(writedownloadFileInfo)] = value;
                OnPropertyChanged();
            }
        }

        public bool write163Info
        {
            get => GetSettings(nameof(write163Info), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(write163Info)] = value;
                OnPropertyChanged();
            }
        }

        public OccupySolution downloadNameOccupySolution
        {
            get => GetSettings(nameof(downloadNameOccupySolution), OccupySolution.Skip);
            set => ApplicationData.Current.LocalSettings.Values[nameof(downloadNameOccupySolution)] = (int)value;
        }

        public string downloadDir
        {
            get
            {
                try
                {
                    return GetSettings(nameof(downloadDir), KnownFolders.MusicLibrary
                        .CreateFolderAsync(nameof(HyPlayer), CreationCollisionOption.OpenIfExists).AsTask().Result
                        .Path);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadDir)] = value;
                OnPropertyChanged();
            }
        }

        public string downloadFileName
        {
            get => GetSettings(nameof(downloadFileName), "{$SINGER} - {$SONGNAME}");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadFileName)] = value;
                OnPropertyChanged();
            }
        }

        public string downloadAudioRate
        {
            get => GetSettings(nameof(downloadAudioRate), "hires");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadAudioRate)] = value;
                OnPropertyChanged();
            }
        }

        public string searchingDir
        {
            get
            {
                try
                {
                    return GetSettings(nameof(searchingDir), downloadDir);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(searchingDir)] = value;
                OnPropertyChanged();
            }
        }

        public int maxDownloadCount
        {
            get => GetSettings(nameof(maxDownloadCount), 1);
            set => ApplicationData.Current.LocalSettings.Values[nameof(maxDownloadCount)] = value;
        }

        public PlayMode songRollType
        {
            get => GetSettings(nameof(songRollType), PlayMode.DefaultRoll);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(songRollType)] = (int)value;
                OnPropertyChanged();
            }
        }

        public bool safeFileAccess
        {
            get => GetSettings(nameof(safeFileAccess), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(safeFileAccess)] = value;
        }

        public List<string> scanLocalFolder
        {
            get
            {
                var folders = GetSettings(nameof(scanLocalFolder), KnownFolders.MusicLibrary.Path);
                return [.. folders.Split("\r\n")];
            }
            set => ApplicationData.Current.LocalSettings.Values[nameof(safeFileAccess)] = string.Join("\r\n", value);
        }

        public bool advancedMusicHistoryStorage
        {
            get => GetSettings(nameof(advancedMusicHistoryStorage), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(advancedMusicHistoryStorage)] = value;
                OnPropertyChanged();
            }
        }

        // ===================================================================
        // Static methods
        // ===================================================================

        public static bool SaveCookies()
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer("LoginedUser", ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var item in Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.Cookies)
            {
                container.Values[item.Key] = item.Value;
            }
            return true;
        }

        public static bool LoadCookies()
        {
            if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("LoginedUser", out var container))
            {
                if (container.Values.Count == 0)
                {
                    return false;
                }
                else
                {
                    foreach (var item in container.Values)
                    {
                        Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.Cookies.Add(item.Key, (string)item.Value);
                    }

                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        // ===================================================================
        // INotifyPropertyChanged infrastructure
        // ===================================================================

#nullable enable
        public event PropertyChangedEventHandler? PropertyChanged;
#nullable restore
        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); ;
            }
            catch
            {
                // ignore
            }
        }

        public static T GetSettings<T>(string propertyName, T defaultValue)
        {
            try
            {
                var success = ApplicationData.Current.LocalSettings.Values.TryGetValue(propertyName, out object value);
                if (success)
                {
                    return (T)value;
                }

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
