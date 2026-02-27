using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.NeteaseApi;
using LiteFM.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace HyPlayer
{
    public partial class Setting : INotifyPropertyChanged
    {
        public ColorGeneratorType ColorGeneratorType
        {
            get => GetSettings(nameof(ColorGeneratorType), ColorGeneratorType.Auto);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ColorGeneratorType)] = (int)value;
                OnPropertyChanged();
            }
        }

        public bool enableAmllTtmlDb
        {
            get => GetSettings(nameof(enableAmllTtmlDb), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableAmllTtmlDb)] = value;
                OnPropertyChanged();
            }
        }

        public string amllTtmlMirrorUrl
        {
            get => GetSettings(nameof(amllTtmlMirrorUrl), "https://gcore.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/ncm-lyrics/[NCM_ID].ttml");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(amllTtmlMirrorUrl)] = value;
                OnPropertyChanged();
            }
        }

        public int lyricPaddingTopRatio
        {
            get => GetSettings(nameof(lyricPaddingTopRatio), 30);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricPaddingTopRatio)] = value;
                OnPropertyChanged();
            }
        }
        public int lyricFadingRatio
        {
            get => GetSettings(nameof(lyricFadingRatio), 5);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricFadingRatio)] = value;
                OnPropertyChanged();
            }
        }


        public AdditionalParameters ApiAdditionalParameters
        {
            get => JsonSerializer.Deserialize<AdditionalParameters>(GetSettings(nameof(ApiAdditionalParameters), "{}"), Common.DefaultOptions) ?? new AdditionalParameters();
            set => ApplicationData.Current.LocalSettings.Values[nameof(ApiAdditionalParameters)] = JsonSerializer.Serialize(value, Common.DefaultOptions);
        }

        public LastFMSession LastFMSession
        {
            get => JsonSerializer.Deserialize<LastFMSession>(GetSettings(nameof(LastFMSession), "{}"), Common.DefaultOptions);
            set
            {
                if (value == null)
                {
                    ApplicationData.Current.LocalSettings.Values[nameof(LastFMSession)] = null;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values[nameof(LastFMSession)] = JsonSerializer.Serialize(value, Common.DefaultOptions);
                }
                OnPropertyChanged();
            }
        }
        public bool UpdateLastFMNowPlaying
        {
            get => GetSettings(nameof(UpdateLastFMNowPlaying), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UpdateLastFMNowPlaying)] = value;
            }
        }
        public bool LastFMScrobble
        {
            get => GetSettings(nameof(LastFMScrobble), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LastFMScrobble)] = value;
            }
        }

        public string lyricFontFamily
        {
            get => GetSettings(nameof(lyricFontFamily), "Microsoft YaHei UI");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricFontFamily)] = value;
            }
        }

        public int lyricLineSpacing
        {
            get => GetSettings(nameof(lyricLineSpacing), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricLineSpacing)] = value;
                OnPropertyChanged();
            }
        }

        public int lyricSize
        {
            get => GetSettings(nameof(lyricSize), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricSize)] = value;
                OnPropertyChanged();
            }
        }

        public int translationSize
        {
            get => GetSettings(nameof(translationSize), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(translationSize)] = value;
                OnPropertyChanged();
            }
        }

        public bool gentleBPMAnimation
        {
            get => GetSettings(nameof(gentleBPMAnimation), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(gentleBPMAnimation)] = value;
                OnPropertyChanged();
            }
        }

        public bool hotlyricOnStartup
        {
            get => GetSettings(nameof(hotlyricOnStartup), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(hotlyricOnStartup)] = value;
                OnPropertyChanged();
            }
        }

        public bool playbarButtonsTransparent
        {
            get => GetSettings(nameof(playbarButtonsTransparent), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarButtonsTransparent)] = value;
                OnPropertyChanged();
            }
        }

        public bool playbarBackgroundElay
        {
            get => GetSettings(nameof(playbarBackgroundElay), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundElay)] = value;
                OnPropertyChanged();
            }
        }

        public bool playButtonAccentColor
        {
            get => GetSettings(nameof(playButtonAccentColor), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playButtonAccentColor)] = value;
                OnPropertyChanged();
            }
        }

        public BackgroundType expandedPlayerBackgroundType
        {
            get => GetSettings(nameof(expandedPlayerBackgroundType), BackgroundType.CoverBlur);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedPlayerBackgroundType)] = (int)value;
                OnPropertyChanged();
            }
        }

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
            //get => GetSettings(nameof(CustomTintOpacity),0);
            //set
            //{
            //    ApplicationData.Current.LocalSettings.Values[nameof(CustomTintOpacity)] = value;
            //    OnPropertyChanged();
            //}
        }

        public double CustomTintLuminosityOpacity
        {
            get
            {
                try
                {
                    if (CustomAcrylic)
                    {
                        return GetSettings<double>(nameof(CustomTintLuminosityOpacity), 3d);
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

        public bool downloadLyric
        {
            get => GetSettings(nameof(downloadLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadLyric)] = value;
                OnPropertyChanged();
            }
        }

        public bool karaokLyric
        {
            get => GetSettings(nameof(karaokLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(karaokLyric)] = value;
                OnPropertyChanged();
            }
        }

        public bool downloadTranslation
        {
            get => GetSettings(nameof(downloadTranslation), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(downloadTranslation)] = value;
                OnPropertyChanged();
            }
        }

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

        public bool displayShuffledList
        {
            get => GetSettings(nameof(displayShuffledList), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(displayShuffledList)] = value;
                OnPropertyChanged();
            }
        }

        public bool useAiDj
        {
            get => GetSettings(nameof(useAiDj), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(useAiDj)] = value;
                OnPropertyChanged();
            }
        }

        public bool EnableCheckTokenApi
        {
            get => GetSettings(nameof(EnableCheckTokenApi), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(EnableCheckTokenApi)] = value;
                Common.NeteaseAPI?.Option.FakeCheckToken = value;
                OnPropertyChanged();
            }
        }

        public bool displayMaintain
        {
            get => GetSettings(nameof(displayMaintain), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(displayMaintain)] = value;
                OnPropertyChanged();
            }
        }

        public bool localProgressiveLoad
        {
            get => GetSettings(nameof(localProgressiveLoad), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(localProgressiveLoad)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricCacheRenderTarget
        {
            get => GetSettings(nameof(lyricCacheRenderTarget), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricCacheRenderTarget)] = value;
                OnPropertyChanged();
            }
        }

        public bool shuffleNoRepeating
        {
            get => GetSettings(nameof(shuffleNoRepeating), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(shuffleNoRepeating)] = value;
                OnPropertyChanged();
                if (HyPlayList.NowPlayType == PlayMode.Shuffled && value) HyPlayList.CreateShufflePlayLists();
            }
        }

        public int lyricScaleSize
        {
            get => GetSettings(nameof(lyricScaleSize), 3);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricScaleSize)] = value;
                OnPropertyChanged();
            }
        }

        public bool forceMemoryGarbage
        {
            get => GetSettings(nameof(forceMemoryGarbage), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(forceMemoryGarbage)] = value;
        }

        public bool expandedUseAcrylic
        {
            get => GetSettings(nameof(expandedUseAcrylic), true);
            set => ApplicationData.Current.LocalSettings.Values[nameof(expandedUseAcrylic)] = value;
        }

        public bool playbarBackgroundBreath
        {
            get => GetSettings(nameof(playbarBackgroundBreath), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundBreath)] = value;
        }

        public bool playbarBackgroundAcrylic
        {
            get => GetSettings(nameof(playbarBackgroundAcrylic), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playbarBackgroundAcrylic)] = value;
                OnPropertyChanged();
            }
        }

        public bool expandAlbumBreath
        {
            get => GetSettings(nameof(expandAlbumBreath), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(expandAlbumBreath)] = value;
        }

        public bool listHeaderAcrylicBlur
        {
            get => GetSettings(nameof(listHeaderAcrylicBlur), true);
            set => ApplicationData.Current.LocalSettings.Values[nameof(listHeaderAcrylicBlur)] = value;
        }

        public bool itemOfListBackgroundAcrylicBlur
        {
            get => GetSettings(nameof(itemOfListBackgroundAcrylicBlur), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(itemOfListBackgroundAcrylicBlur)] = value;
        }

        public bool lyricDropshadow
        {
            get => GetSettings(nameof(lyricDropshadow), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(lyricDropshadow)] = value;
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

        public LyricColor lyricColor
        {
            get => GetSettings(nameof(lyricColor), LyricColor.Auto);
            set => ApplicationData.Current.LocalSettings.Values[nameof(lyricColor)] = (int)value;
        }

        public OccupySolution downloadNameOccupySolution
        {
            get => GetSettings(nameof(downloadNameOccupySolution), OccupySolution.Skip);
            set => ApplicationData.Current.LocalSettings.Values[nameof(downloadNameOccupySolution)] = (int)value;
        }


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

        public bool greedlyLoadPlayContainerItems
        {
            get => GetSettings(nameof(greedlyLoadPlayContainerItems), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(greedlyLoadPlayContainerItems)] = value;
                OnPropertyChanged();
            }
        }

        public bool AutoAddGreedilyLoadedSongsToPlayList
        {
            get => GetSettings(nameof(AutoAddGreedilyLoadedSongsToPlayList), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(AutoAddGreedilyLoadedSongsToPlayList)] = value;
                OnPropertyChanged();
            }
        }

        public int albumBorderLength
        {
            get => GetSettings(nameof(albumBorderLength), 0);
            set => ApplicationData.Current.LocalSettings.Values[nameof(albumBorderLength)] = value;
        }

        public int romajiSize
        {
            get => GetSettings(nameof(romajiSize), 15);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(romajiSize)] = value;
                OnPropertyChanged();
            }
        }



        public bool noImage
        {
            get => GetSettings(nameof(noImage), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(noImage)] = value;
        }

        public LyricAlignment lyricAlignment
        {
            get => GetSettings(nameof(lyricAlignment), LyricAlignment.Left);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricAlignment)] = (int)value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderFocusHighlighting
        {
            get => GetSettings(nameof(lyricRenderFocusHighlighting), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderFocusHighlighting)] = value;
                OnPropertyChanged();
            }
        }

        public int lyricRenderWidthRatio
        {
            get => GetSettings(nameof(lyricRenderWidthRatio), 80);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderWidthRatio)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderTransliterationScanning
        {
            get => GetSettings(nameof(lyricRenderTransliterationScanning), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderTransliterationScanning)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderSimpleLineScanning
        {
            get => GetSettings(nameof(lyricRenderSimpleLineScanning), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderSimpleLineScanning)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderScaleWhenFocusing
        {
            get => GetSettings(nameof(lyricRenderScaleWhenFocusing), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderScaleWhenFocusing)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderBlur
        {
            get => GetSettings(nameof(lyricRenderBlur), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderBlur)] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricRenderFade
        {
            get => GetSettings(nameof(lyricRenderFade), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricRenderFade)] = value;
                OnPropertyChanged();
            }
        }
        public bool EnableFFT
        {
            get => GetSettings(nameof(EnableFFT), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(EnableFFT)] = value;
                HyPlayList.Player?.EnableFFTProcessing = value;
                OnPropertyChanged();
            }
        }
#nullable enable
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


        public bool jumpVipSongPlaying
        {
            get => GetSettings(nameof(jumpVipSongPlaying), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(jumpVipSongPlaying)] = value;
                OnPropertyChanged();
            }
        }

        public bool jumpVipSongDownloading
        {
            get => GetSettings(nameof(jumpVipSongDownloading), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(jumpVipSongDownloading)] = value;
                OnPropertyChanged();
            }
        }

        public string audioRate
        {
            get => GetSettings(nameof(audioRate), "exhigh");
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(audioRate)] = value;
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

        public bool xboxHidePointer
        {
            get => GetSettings(nameof(xboxHidePointer), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(xboxHidePointer)] = value;
        }

        public bool enableTouchGestureAction
        {
            get => GetSettings(nameof(enableTouchGestureAction), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(enableTouchGestureAction)] = value;
        }

        public bool highPreciseLyricTimer
        {
            get => GetSettings(nameof(highPreciseLyricTimer), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(highPreciseLyricTimer)] = value;
        }

        public GestureMode gestureMode
        {
            get => GetSettings(nameof(gestureMode), GestureMode.Basic);
            set => ApplicationData.Current.LocalSettings.Values[nameof(gestureMode)] = (int)value;
        }

        public int maxDownloadCount
        {
            get => GetSettings(nameof(maxDownloadCount), 1);
            set => ApplicationData.Current.LocalSettings.Values[nameof(maxDownloadCount)] = value;
        }

        public int Volume
        {
            get
            {
                try
                {
                    return GetSettings(nameof(Volume), 50);
                }
                catch
                {
                    return 50;
                }
            }

            set => ApplicationData.Current.LocalSettings.Values[nameof(Volume)] = value;
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

        public string cacheDir
        {
            get
            {
                try
                {
                    return GetSettings(nameof(cacheDir), ApplicationData.Current.LocalCacheFolder.Path);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(cacheDir)] = value;
                OnPropertyChanged();
            }
        }

        public bool CrossFade
        {
            get => GetSettings(nameof(CrossFade), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["CrossFade"] = value;
                OnPropertyChanged();
            }
        }

        public bool notClearMode
        {
            get => GetSettings(nameof(notClearMode), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(notClearMode)] = value;
                OnPropertyChanged();
            }
        }

        public bool AutoHidePlaybar
        {
            get => GetSettings(nameof(AutoHidePlaybar), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(AutoHidePlaybar)] = value;
                OnPropertyChanged();
            }
        }
        public int AutoHidePlaybarTime
        {
            get => GetSettings(nameof(AutoHidePlaybarTime), 3);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(AutoHidePlaybarTime)] = value;
                Common.PlaybarSecondCounter = 0;
                OnPropertyChanged();
            }
        }

        public bool useTaglibPicture
        {
            get => GetSettings(nameof(useTaglibPicture), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(useTaglibPicture)] = value;
                OnPropertyChanged();
            }
        }

        public bool showComposerInLyric
        {
            get => GetSettings(nameof(showComposerInLyric), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(showComposerInLyric)] = value;
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

        public double CrossFadeTime
        {
            get => GetSettings(nameof(CrossFadeTime), 3d);

            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(CrossFadeTime)] = value;
            }
        }

        public bool playBarMargin
        {
            get => GetSettings(nameof(playBarMargin), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(playBarMargin)] = value;
                OnPropertyChanged();
            }
        }

        public bool expandAnimation
        {
            get => GetSettings(nameof(expandAnimation), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandAnimation)] = value ? "true" : "false";
                OnPropertyChanged();
            }
        }

        public bool uiSound
        {
            get => GetSettings(nameof(uiSound), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(uiSound)] = value;
                OnPropertyChanged();
            }
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

        public bool songUrlLazyGet
        {
            get => GetSettings(nameof(songUrlLazyGet), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(songUrlLazyGet)] = value;
                OnPropertyChanged();
            }
        }

        public bool enableCache
        {
            get => GetSettings(nameof(enableCache), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableCache)] = value;
                OnPropertyChanged();
            }
        }

        public bool enableApiCache
        {
            get => GetSettings(nameof(enableApiCache), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableApiCache)] = value;
                OnPropertyChanged();
            }
        }

        public bool highQualityCoverInSMTC
        {
            get => GetSettings(nameof(highQualityCoverInSMTC), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(highQualityCoverInSMTC)] = value;
                OnPropertyChanged();
            }
        }

        public bool acrylicAvailabiliity => new UISettings().AdvancedEffectsEnabled && Windows.UI.Composition.CompositionCapabilities.GetForCurrentView().AreEffectsFast();


        public bool expandedPlayerFullCover
        {
            get => GetSettings(nameof(expandedPlayerFullCover), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedPlayerFullCover)] = value;
                OnPropertyChanged();
            }
        }

        public ThemeRequest themeRequest
        {
            // 0 - 未设置   1 - 浅色  2 - 深色
            get => GetSettings(nameof(themeRequest), ThemeRequest.Auto);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(themeRequest)] = (int)value;
                OnPropertyChanged();
            }
        }

        public bool IsOldThemeEnabled
        {
            get => GetSettings(nameof(IsOldThemeEnabled), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsOldThemeEnabled)] = value;
                OnPropertyChanged();
            }
        }

        public int expandedCoverShadowDepth
        {
            get => GetSettings(nameof(expandedCoverShadowDepth), 4);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(expandedCoverShadowDepth)] = value;
                OnPropertyChanged();
            }
        }

        public string AudioRenderDevice
        {
            get => GetSettings("AudioRenderDeviceID", "");
            set
            {
                ApplicationData.Current.LocalSettings.Values["AudioRenderDeviceID"] = value;
                _ = HyPlayList.OnAudioRenderDeviceChangedOrInitialized();
                OnPropertyChanged();
            }
        }

        public bool DisablePopUp
        {
            get => GetSettings(nameof(DisablePopUp), false);
            set => ApplicationData.Current.LocalSettings.Values[nameof(DisablePopUp)] = value;
        }

        public UpdateSource UpdateSource
        {
            get => GetSettings(nameof(UpdateSource), UpdateSource.Release);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UpdateSource)] = (int)value;
                OnPropertyChanged();
            }
        }

        public bool enableTile
        {
            get => GetSettings(nameof(enableTile), Environment.OSVersion.Version.Build < 22000);
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

        public bool canaryChannelAvailability
        {
            get => GetSettings(nameof(canaryChannelAvailability), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(canaryChannelAvailability)] = value;
                OnPropertyChanged();
            }
        }

        public bool tileBackgroundAvailability
        {
            get => GetSettings(nameof(tileBackgroundAvailability), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(tileBackgroundAvailability)] = value;
                OnPropertyChanged();
            }
        }

        public bool saveTileBackgroundToLocalFolder
        {
            get => GetSettings(nameof(saveTileBackgroundToLocalFolder), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(saveTileBackgroundToLocalFolder)] = value;
                OnPropertyChanged();
            }
        }

        public bool animationAdaptBPM
        {
            get => GetSettings(nameof(animationAdaptBPM), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(animationAdaptBPM)] = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan ABStartPoint
        {
            get => _abStartPoint;
            set
            {
                _abStartPoint = value;
                OnPropertyChanged(nameof(ABStartPointFriendlyValue));
            }
        }

        public string ABStartPointFriendlyValue
        {
            get
            {
                if (_abStartPoint.Hours == 0)
                {
                    if (_abStartPoint.Minutes < 10)
                        return _abStartPoint.ToString(@"m\:ss") ?? string.Empty;
                    else
                        return _abStartPoint.ToString(@"mm\:ss") ?? string.Empty;
                }
                else
                {
                    return _abStartPoint.ToString(@"hh\:mm\:ss") ?? string.Empty;
                }
            }
        }

        private TimeSpan _abStartPoint = TimeSpan.Zero;

        public TimeSpan ABEndPoint
        {
            get => _abEndPoint;
            set
            {
                _abEndPoint = value;
                OnPropertyChanged(nameof(ABEndPointFriendlyValue));
            }
        }

        private TimeSpan _abEndPoint = TimeSpan.Zero;

        public string ABEndPointFriendlyValue
        {
            get
            {
                if (_abEndPoint.Hours == 0)
                {
                    if (_abEndPoint.Minutes < 10)
                        return _abStartPoint.ToString(@"m\:ss") ?? string.Empty;
                    else
                        return _abStartPoint.ToString(@"mm\:ss") ?? string.Empty;
                }
                else
                {
                    return _abStartPoint.ToString(@"hh\:mm\:ss") ?? string.Empty;
                }
            }
        }

        public bool ABRepeatStatus
        {
            get => _abRepeatStatus;
            set
            {
                _abRepeatStatus = value;
                if (value) HyPlayList.OnPlayPositionChange += HyPlayList.CheckABTimeRemaining;
                else HyPlayList.OnPlayPositionChange -= HyPlayList.CheckABTimeRemaining;
                OnPropertyChanged();
            }
        }

        private static bool _abRepeatStatus = false;

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

        public bool EnableTitleBarImmerse
        {
            get => GetSettings("enableTitleBarImmerse", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["enableTitleBarImmerse"] = value;
                OnPropertyChanged();
            }
        }

        public RomajiSource LyricRomajiSource
        {
            //  0 - 不进行转换  1 - 自动选择  2 - 网易云优先  3 - Kawazu 转换优先
            get => GetSettings(nameof(LyricRomajiSource), RomajiSource.None);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRomajiSource)] = (int)value;
                OnPropertyChanged();
            }
        }

        public RollingCalculator LineRollingCalculator
        {
            get => GetSettings(nameof(LineRollingCalculator), RollingCalculator.ElasticEaseRollingCalculator);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LineRollingCalculator)] = (int)value;
                OnPropertyChanged();
            }
        }



        public bool UseHttp
        {
            get => GetSettings(nameof(UseHttp), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UseHttp)] = value;
                OnPropertyChanged();
            }
        }
        public bool UseHttpWhenGettingSongs
        {
            get => GetSettings(nameof(UseHttpWhenGettingSongs), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UseHttpWhenGettingSongs)] = value;
                OnPropertyChanged();
            }
        }
        public bool EnableAudioGain
        {
            get => GetSettings(nameof(EnableAudioGain), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(EnableAudioGain)] = value;
                OnPropertyChanged();
                if (HyPlayList.Player.PrimaryPlaybackSource != null)
                {
                    if (value)
                    {
                        HyPlayList.Player.SetPlaybackSourceOutputVolume(HyPlayList.NowPlayingItem?.Volume ?? 1, HyPlayList.Player.PrimaryPlaybackSource);
                    }
                    else HyPlayList.Player.SetPlaybackSourceOutputVolume(1, HyPlayList.Player.PrimaryPlaybackSource);
                }
            }
        }
        public bool CompactPlayerPageBlurStatus
        {
            get => GetSettings(nameof(CompactPlayerPageBlurStatus), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(CompactPlayerPageBlurStatus)] = value;
                OnPropertyChanged();
            }
        }
        public bool EnableProxy
        {
            get => GetSettings(nameof(EnableProxy), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(EnableProxy)] = value;
                OnPropertyChanged();
            }
        }
        public bool MigrateLyrics
        {
            get => GetSettings(nameof(MigrateLyrics), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(MigrateLyrics)] = value;
                OnPropertyChanged();
            }
        }

        public bool OptimizeLyric
        {
            get => GetSettings(nameof(OptimizeLyric), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(OptimizeLyric)] = value;
                OnPropertyChanged();
            }
        }

        public bool LyricRendererDebugMode
        {
            get => GetSettings(nameof(LyricRendererDebugMode), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRendererDebugMode)] = value;
                OnPropertyChanged();
            }
        }
        public bool IsolationFullThrottle
        {
            get => GetSettings(nameof(IsolationFullThrottle), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationFullThrottle)] = value;
                OnPropertyChanged();
            }
        }
        public double IsolationFPS
        {
            get => Math.Max(GetSettings(nameof(IsolationFPS), 60d), 60d);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationFPS)] = value;
                OnPropertyChanged();
            }
        }
        public int LyricRendererFPS
        {
            get => GetSettings(nameof(LyricRendererFPS), 60);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LyricRendererFPS)] = value;
                OnPropertyChanged();
            }
        }
        public float IsolationScale
        {
            get => GetSettings(nameof(IsolationScale), 1f);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationScale)] = value;
                OnPropertyChanged();
            }
        }
        public bool IsolationLightWave
        {
            get => GetSettings(nameof(IsolationLightWave), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(IsolationLightWave)] = value;
                OnPropertyChanged();
            }
        }
        public bool ImpressionistLABSpace
        {
            get => GetSettings(nameof(ImpressionistLABSpace), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistLABSpace)] = value;
                OnPropertyChanged();
            }
        }
        public bool ImpressionistIgnoreWhite
        {
            get => GetSettings(nameof(ImpressionistIgnoreWhite), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistIgnoreWhite)] = value;
                OnPropertyChanged();
            }
        }
        public bool ImpressionistUseKMeansPP
        {
            get => GetSettings(nameof(ImpressionistUseKMeansPP), true);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ImpressionistUseKMeansPP)] = value;
                OnPropertyChanged();
            }
        }

        public static bool SaveCookies()
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer("LoginedUser", ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var item in Common.NeteaseAPI.Option.Cookies)
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
                        Common.NeteaseAPI.Option.Cookies.Add(item.Key, (string)item.Value);
                    }

                    return true;
                }
            }
            else
            {
                return false;
            }
        }

#nullable enable
        public event PropertyChangedEventHandler? PropertyChanged;
#nullable restore
        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            try
            {
                await Common.Invoke(() => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
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
                if(success)
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
