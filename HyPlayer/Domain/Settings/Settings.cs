using CommunityToolkit.Mvvm.DependencyInjection;
using LiteFM.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI;
using HyPlayerUISettings = HyPlayer.Domain.Settings.UISettings;

namespace HyPlayer.Domain.Settings
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

        public int Volume { get => Playback.Volume; set { Playback.Volume = value; OnPropertyChanged(); } }
        public string audioRate { get => Playback.audioRate; set { Playback.audioRate = value; OnPropertyChanged(); } }
        public string TransitionId
        {
            get => Playback.TransitionId;
            set
            {
                Playback.TransitionId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCrossFadeTransition));
            }
        }
        public bool IsCrossFadeTransition => TransitionId == "xfd";
        public double CrossFadeTime { get => Playback.CrossFadeTime; set { Playback.CrossFadeTime = value; OnPropertyChanged(); } }
        public bool EnableAudioGain { get => Playback.EnableAudioGain; set { Playback.EnableAudioGain = value; OnPropertyChanged(); } }
        public bool ABRepeatStatus { get => Playback.ABRepeatStatus; set { Playback.ABRepeatStatus = value; OnPropertyChanged(); } }
        public TimeSpan ABStartPoint { get => Playback.ABStartPoint; set { Playback.ABStartPoint = value; OnPropertyChanged(); } }
        public string ABStartPointFriendlyValue => Playback.ABStartPointFriendlyValue;
        public TimeSpan ABEndPoint { get => Playback.ABEndPoint; set { Playback.ABEndPoint = value; OnPropertyChanged(); } }
        public string ABEndPointFriendlyValue => Playback.ABEndPointFriendlyValue;
        public bool enableCache { get => Playback.enableCache; set { Playback.enableCache = value; OnPropertyChanged(); } }
        public string cacheDir { get => Playback.cacheDir; set { Playback.cacheDir = value; OnPropertyChanged(); } }
        public string AudioRenderDevice { get => Playback.AudioRenderDevice; set { Playback.AudioRenderDevice = value; OnPropertyChanged(); } }
        public bool EnableFFT { get => Playback.EnableFFT; set { Playback.EnableFFT = value; OnPropertyChanged(); } }
        public string ActiveStrategyId { get => Playback.ActiveStrategyId; set { Playback.ActiveStrategyId = value; OnPropertyChanged(); } }

        // --- UISettings delegates ---

        public ThemeRequest themeRequest { get => UI.themeRequest; set { UI.themeRequest = value; OnPropertyChanged(); } }
        public bool expandAnimation { get => UI.expandAnimation; set { UI.expandAnimation = value; OnPropertyChanged(); } }
        public bool noImage { get => UI.noImage; set { UI.noImage = value; OnPropertyChanged(); } }
        public LyricAlignment lyricAlignment { get => UI.lyricAlignment; set { UI.lyricAlignment = value; OnPropertyChanged(); } }
        public int lyricSize { get => UI.lyricSize; set { UI.lyricSize = value; OnPropertyChanged(); } }
        public LyricColor lyricColor { get => UI.lyricColor; set { UI.lyricColor = value; OnPropertyChanged(); } }
        public ColorGeneratorType ColorGeneratorType { get => UI.ColorGeneratorType; set { UI.ColorGeneratorType = value; OnPropertyChanged(); } }
        public BackgroundType expandedPlayerBackgroundType { get => UI.expandedPlayerBackgroundType; set { UI.expandedPlayerBackgroundType = value; OnPropertyChanged(); } }
        public bool albumRotate { get => UI.albumRotate; set { UI.albumRotate = value; OnPropertyChanged(); } }
        public bool albumRound { get => UI.albumRound; set { UI.albumRound = value; OnPropertyChanged(); } }
        public int albumBorderLength { get => UI.albumBorderLength; set { UI.albumBorderLength = value; OnPropertyChanged(); } }
        public bool expandedUseAcrylic { get => UI.expandedUseAcrylic; set { UI.expandedUseAcrylic = value; OnPropertyChanged(); } }
        public bool expandAlbumBreath { get => UI.expandAlbumBreath; set { UI.expandAlbumBreath = value; OnPropertyChanged(); } }
        public bool expandedPlayerFullCover { get => UI.expandedPlayerFullCover; set { UI.expandedPlayerFullCover = value; OnPropertyChanged(); } }
        public int expandedCoverShadowDepth { get => UI.expandedCoverShadowDepth; set { UI.expandedCoverShadowDepth = value; OnPropertyChanged(); } }
        public bool CompactPlayerPageBlurStatus { get => UI.CompactPlayerPageBlurStatus; set { UI.CompactPlayerPageBlurStatus = value; OnPropertyChanged(); } }
        public bool notClearMode { get => UI.notClearMode; set { UI.notClearMode = value; OnPropertyChanged(); } }
        public bool AutoHidePlaybar { get => UI.AutoHidePlaybar; set { UI.AutoHidePlaybar = value; OnPropertyChanged(); } }
        public int AutoHidePlaybarTime { get => UI.AutoHidePlaybarTime; set { UI.AutoHidePlaybarTime = value; OnPropertyChanged(); } }
        public bool playBarMargin { get => UI.playBarMargin; set { UI.playBarMargin = value; OnPropertyChanged(); } }
        public bool uiSound { get => UI.uiSound; set { UI.uiSound = value; OnPropertyChanged(); } }
        public bool displayShuffledList { get => UI.displayShuffledList; set { UI.displayShuffledList = value; OnPropertyChanged(); } }
        public bool displayMaintain { get => UI.displayMaintain; set { UI.displayMaintain = value; OnPropertyChanged(); } }
        public bool enableTouchGestureAction { get => UI.enableTouchGestureAction; set { UI.enableTouchGestureAction = value; OnPropertyChanged(); } }
        public GestureMode gestureMode { get => UI.gestureMode; set { UI.gestureMode = value; OnPropertyChanged(); } }
        public bool animationAdaptBPM { get => UI.animationAdaptBPM; set { UI.animationAdaptBPM = value; OnPropertyChanged(); } }
        public bool gentleBPMAnimation { get => UI.gentleBPMAnimation; set { UI.gentleBPMAnimation = value; OnPropertyChanged(); } }
        public bool DisablePopUp { get => UI.DisablePopUp; set { UI.DisablePopUp = value; OnPropertyChanged(); } }
        public bool canaryChannelAvailability { get => UI.canaryChannelAvailability; set { UI.canaryChannelAvailability = value; OnPropertyChanged(); } }
        public bool localProgressiveLoad { get => UI.localProgressiveLoad; set { UI.localProgressiveLoad = value; OnPropertyChanged(); } }
        public UpdateSource UpdateSource { get => UI.UpdateSource; set { UI.UpdateSource = value; OnPropertyChanged(); } }
        public bool EnableTile { get => UI.EnableTile; set { UI.EnableTile = value; OnPropertyChanged(); } }
        public bool EnableTileBackground { get => UI.EnableTileBackground; set { UI.EnableTileBackground = value; OnPropertyChanged(); } }

        // --- ApiSettings delegates ---

        public bool EnableProxy { get => Api.EnableProxy; set { Api.EnableProxy = value; OnPropertyChanged(); } }
        public string ApiAdditionalParametersJson { get => Api.ApiAdditionalParametersJson; set { Api.ApiAdditionalParametersJson = value; OnPropertyChanged(); } }
        public bool UseHttp { get => Api.UseHttp; set { Api.UseHttp = value; OnPropertyChanged(); } }
        public bool EnableCheckTokenApi { get => Api.EnableCheckTokenApi; set { Api.EnableCheckTokenApi = value; OnPropertyChanged(); } }
        public bool enableApiCache { get => Api.enableApiCache; set { Api.enableApiCache = value; OnPropertyChanged(); } }
        public bool songUrlLazyGet { get => Api.songUrlLazyGet; set { Api.songUrlLazyGet = value; OnPropertyChanged(); } }
        public bool greedlyLoadPlayContainerItems { get => Api.greedlyLoadPlayContainerItems; set { Api.greedlyLoadPlayContainerItems = value; OnPropertyChanged(); } }
        public bool AutoAddGreedilyLoadedSongsToPlayList { get => Api.AutoAddGreedilyLoadedSongsToPlayList; set { Api.AutoAddGreedilyLoadedSongsToPlayList = value; OnPropertyChanged(); } }
        public bool jumpVipSongPlaying { get => Api.jumpVipSongPlaying; set { Api.jumpVipSongPlaying = value; OnPropertyChanged(); } }
        public bool jumpVipSongDownloading { get => Api.jumpVipSongDownloading; set { Api.jumpVipSongDownloading = value; OnPropertyChanged(); } }

        // --- LyricSettings delegates ---

        public RomajiSource LyricRomajiSource { get => Lyric.LyricRomajiSource; set { Lyric.LyricRomajiSource = value; OnPropertyChanged(); } }
        public bool showComposerInLyric { get => Lyric.showComposerInLyric; set { Lyric.showComposerInLyric = value; OnPropertyChanged(); } }
        public bool downloadLyric { get => Lyric.downloadLyric; set { Lyric.downloadLyric = value; OnPropertyChanged(); } }
        public bool downloadTranslation { get => Lyric.downloadTranslation; set { Lyric.downloadTranslation = value; OnPropertyChanged(); } }
        public bool MigrateLyrics { get => Lyric.MigrateLyrics; set { Lyric.MigrateLyrics = value; OnPropertyChanged(); } }
        public bool OptimizeLyric { get => Lyric.OptimizeLyric; set { Lyric.OptimizeLyric = value; OnPropertyChanged(); } }
        public bool lyricDropshadow { get => Lyric.lyricDropshadow; set { Lyric.lyricDropshadow = value; OnPropertyChanged(); } }
        public bool lyricCacheRenderTarget { get => Lyric.lyricCacheRenderTarget; set { Lyric.lyricCacheRenderTarget = value; OnPropertyChanged(); } }
        public int lyricScaleSize { get => Lyric.lyricScaleSize; set { Lyric.lyricScaleSize = value; OnPropertyChanged(); } }
        public int lyricLineSpacing { get => Lyric.lyricLineSpacing; set { Lyric.lyricLineSpacing = value; OnPropertyChanged(); } }
        public int translationSize { get => Lyric.translationSize; set { Lyric.translationSize = value; OnPropertyChanged(); } }
        public int romajiSize { get => Lyric.romajiSize; set { Lyric.romajiSize = value; OnPropertyChanged(); } }
        public int sublineLyricSize { get => Lyric.sublineLyricSize; set { Lyric.sublineLyricSize = value; OnPropertyChanged(); } }
        public int sublineTranslationSize { get => Lyric.sublineTranslationSize; set { Lyric.sublineTranslationSize = value; OnPropertyChanged(); } }
        public int sublineRomajiSize { get => Lyric.sublineRomajiSize; set { Lyric.sublineRomajiSize = value; OnPropertyChanged(); } }
        public int lyricPaddingTopRatio { get => Lyric.lyricPaddingTopRatio; set { Lyric.lyricPaddingTopRatio = value; OnPropertyChanged(); } }
        public int lyricFadingRatio { get => Lyric.lyricFadingRatio; set { Lyric.lyricFadingRatio = value; OnPropertyChanged(); } }
        public bool hotlyricOnStartup { get => Lyric.hotlyricOnStartup; set { Lyric.hotlyricOnStartup = value; OnPropertyChanged(); } }
        public bool enableAmllTtmlDb { get => Lyric.enableAmllTtmlDb; set { Lyric.enableAmllTtmlDb = value; OnPropertyChanged(); } }
        public string amllTtmlMirrorUrl { get => Lyric.amllTtmlMirrorUrl; set { Lyric.amllTtmlMirrorUrl = value; OnPropertyChanged(); } }
        public bool lyricRenderFocusHighlighting { get => Lyric.lyricRenderFocusHighlighting; set { Lyric.lyricRenderFocusHighlighting = value; OnPropertyChanged(); } }
        public int lyricRenderWidthRatio { get => Lyric.lyricRenderWidthRatio; set { Lyric.lyricRenderWidthRatio = value; OnPropertyChanged(); } }
        public bool lyricRenderTransliterationScanning { get => Lyric.lyricRenderTransliterationScanning; set { Lyric.lyricRenderTransliterationScanning = value; OnPropertyChanged(); } }
        public bool lyricRenderSimpleLineScanning { get => Lyric.lyricRenderSimpleLineScanning; set { Lyric.lyricRenderSimpleLineScanning = value; OnPropertyChanged(); } }
        public LyricScanStyle lyricRenderScanStyle { get => Lyric.lyricRenderScanStyle; set { Lyric.lyricRenderScanStyle = value; OnPropertyChanged(); } }
        public bool lyricRenderScaleWhenFocusing { get => Lyric.lyricRenderScaleWhenFocusing; set { Lyric.lyricRenderScaleWhenFocusing = value; OnPropertyChanged(); } }
        public bool lyricRenderTransform3D { get => Lyric.lyricRenderTransform3D; set { Lyric.lyricRenderTransform3D = value; OnPropertyChanged(); } }
        public bool lyricRenderBlur { get => Lyric.lyricRenderBlur; set { Lyric.lyricRenderBlur = value; OnPropertyChanged(); } }
        public bool lyricRenderFade { get => Lyric.lyricRenderFade; set { Lyric.lyricRenderFade = value; OnPropertyChanged(); } }
        public RollingCalculator LineRollingCalculator { get => Lyric.LineRollingCalculator; set { Lyric.LineRollingCalculator = value; OnPropertyChanged(); } }
        public bool LyricRendererDebugMode { get => Lyric.LyricRendererDebugMode; set { Lyric.LyricRendererDebugMode = value; OnPropertyChanged(); } }
#nullable enable
        public Color? pureLyricIdleColor { get => Lyric.pureLyricIdleColor; set { Lyric.pureLyricIdleColor = value; OnPropertyChanged(); } }
        public Color? pureLyricFocusingColor { get => Lyric.pureLyricFocusingColor; set { Lyric.pureLyricFocusingColor = value; OnPropertyChanged(); } }
        public Color? karaokLyricFocusingColor { get => Lyric.karaokLyricFocusingColor; set { Lyric.karaokLyricFocusingColor = value; OnPropertyChanged(); } }
#nullable restore
        public bool IsolationFullThrottle { get => Lyric.IsolationFullThrottle; set { Lyric.IsolationFullThrottle = value; OnPropertyChanged(); } }
        public double IsolationFPS { get => Lyric.IsolationFPS; set { Lyric.IsolationFPS = value; OnPropertyChanged(); } }
        public float IsolationScale { get => Lyric.IsolationScale; set { Lyric.IsolationScale = value; OnPropertyChanged(); } }
        public bool IsolationLightWave { get => Lyric.IsolationLightWave; set { Lyric.IsolationLightWave = value; OnPropertyChanged(); } }
        // --- LastFMSettings delegates ---

        public LastFMSession LastFMSession { get => LastFM.LastFMSession; set { LastFM.LastFMSession = value; OnPropertyChanged(); } }
        public bool UpdateLastFMNowPlaying { get => LastFM.UpdateLastFMNowPlaying; set { LastFM.UpdateLastFMNowPlaying = value; OnPropertyChanged(); } }
        public bool LastFMScrobble { get => LastFM.LastFMScrobble; set { LastFM.LastFMScrobble = value; OnPropertyChanged(); } }
        public bool useAiDj { get => LastFM.useAiDj; set { LastFM.useAiDj = value; OnPropertyChanged(); } }

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
            set { ApplicationData.Current.LocalSettings.Values[nameof(downloadNameOccupySolution)] = (int)value; OnPropertyChanged(); }
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
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(maxDownloadCount)] = value;
                OnPropertyChanged();
            }
        }

        public List<string> scanLocalFolder
        {
            get
            {
                var folders = GetSettings(nameof(scanLocalFolder), KnownFolders.MusicLibrary.Path);
                return [.. folders.Split("\r\n")];
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(scanLocalFolder)] = string.Join("\r\n", value);
                OnPropertyChanged();
            }
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

        public string lyricFontFamily
        {
            get => GetSettings(nameof(lyricFontFamily), "Microsoft YaHei UI");
            set
            {
                if (lyricFontFamily == value) return;
                ApplicationData.Current.LocalSettings.Values[nameof(lyricFontFamily)] = value;
                OnPropertyChanged();
            }
        }

        // ===================================================================
        // Static methods
        // ===================================================================

        public static bool SaveCookies(IReadOnlyDictionary<string, string> sessionValues)
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer("LoginedUser", ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var item in sessionValues)
            {
                container.Values[item.Key] = item.Value;
            }
            return true;
        }

        public static IReadOnlyDictionary<string, string> LoadCookies()
        {
            if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("LoginedUser", out var container))
            {
                if (container.Values.Count == 0)
                {
                    return new Dictionary<string, string>();
                }
                else
                {
                    var values = new Dictionary<string, string>();
                    foreach (var item in container.Values)
                    {
                        if (item.Value is string value)
                            values[item.Key] = value;
                    }

                    return values;
                }
            }
            else
            {
                return new Dictionary<string, string>();
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
