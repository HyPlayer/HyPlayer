#region

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using Kawazu;
using Microsoft.Toolkit.Uwp.UI;
using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IF.Lastfm.Core.Api;
#if !DEBUG
using Microsoft.AppCenter.Crashes;
#endif

#endregion

namespace HyPlayer
{
    internal static class Common
    {
        public delegate void EnterForegroundFromBackgroundEvent();

        public static CloudMusicApi ncapi = new();
        public static bool Logined = false;
        public static bool IsInFm = false;
        public static NCUser? LoginedUser;
        public static bool IsInBackground = false;
        public static ExpandedPlayer? PageExpandedPlayer;
        public static MainPage PageMain;
        public static PlayBar BarPlayBar;
        public static Frame BaseFrame;
        public static BasePage PageBase;
        public static Setting Setting = new();
        public static bool ShowLyricSound = true;
        public static bool ShowLyricTrans = true;
        public static List<string> LikedSongs = new();
        public static KawazuConverter? KawazuConv;
        public static List<NCPlayList> MySongLists = new();
        public static readonly Stack<NavigationHistoryItem> NavigationHistory = new();
        public static bool isExpanded = false;
        public static TeachingTip GlobalTip;
        public static readonly Stack<KeyValuePair<string, string?>> TeachingTipList = new();
        private static object previousNavigationItem;
        public static List<string> ErrorMessageList = new();
        public static EnterForegroundFromBackgroundEvent OnEnterForegroundFromBackground;
        public static ObservableCollection<string> Logs = new();
        public static bool NavigatingBack;
        private static int _teachingTipSecondCounter = 3;

        public static IAsyncAction Invoke(Action action,
            CoreDispatcherPriority Priority = CoreDispatcherPriority.Normal)
        {
            if (!IsInBackground)
                try
                {
                    if (CoreApplication.Views.Count > 0)
                        return CoreApplication.MainView.Dispatcher.RunAsync(Priority,
                            () => { action(); });
                }
#if DEBUG
                catch
                {
#else
                catch (Exception e)
                {
                    Crashes.TrackError(e, null,
                        ErrorAttachmentLog.AttachmentWithText(e.InnerException?.ToString(), "inner"));
#endif

                    /*
                    Invoke((async () =>
                    {
                        await new ContentDialog
                        {
                            Title = "发生错误",
                            Content = "Error: " + e.Message + "\r\n" + e.StackTrace,
                            CloseButtonText = "关闭",
                            DefaultButton = ContentDialogButton.Close
                        }.ShowAsync();
                    }));
                    */
                }

            return null;
        }


        public static void AddToTeachingTipLists(string title, string subtitle = "")
        {
            TeachingTipList.Push(new KeyValuePair<string, string?>(title, subtitle));
            _ = Invoke(() =>
            {
                if (!GlobalTip.IsOpen)
                    RollTeachingTip(false);
            });
        }

        public static void RollTeachingTip(bool passiveRoll = true)
        {
            if (passiveRoll && _teachingTipSecondCounter-- > 0) return;
            _teachingTipSecondCounter = 3;
            if (TeachingTipList.Count == 0)
            {
                _ = Invoke(() => GlobalTip.IsOpen = false); //在显示完列表中所有的TeachingTip之后关闭TeachingTip
                return;
            }

            _ = Invoke(() =>
            {
                if (TeachingTipList.Count == 0) return;
                var (title, subtitle) = TeachingTipList.Pop(); // deconstruction
                GlobalTip.Title = title;
                GlobalTip.Subtitle = subtitle ?? "";
                if (!GlobalTip.IsOpen)
                {
                    GlobalTip.IsOpen = true;
                }
                else
                {
                    GlobalTip.IsOpen = false;
                    GlobalTip.IsOpen = true;
                }
            });
        }

        public static void NavigatePage(Type SourcePageType, object paratmer = null, object ignore = null)
        {
            if (Setting.forceMemoryGarbage)
            {
                if (NavigationHistory.Count >= 1 && PageBase.NavMain.SelectedItem == NavigationHistory.Peek().Item)
                    PageBase.NavMain.SelectedItem = PageBase.NavItemBlank;
                NavigationHistory.Push(new NavigationHistoryItem
                {
                    PageType = SourcePageType,
                    Paratmers = paratmer,
                    Item = PageBase.NavMain.SelectedItem
                });
                BaseFrame?.Navigate(SourcePageType, paratmer,
                    new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
                GC.Collect();
            }
            else
            {
                BaseFrame?.Navigate(SourcePageType, paratmer,
                    new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
            }

            if (previousNavigationItem == PageBase.NavMain.SelectedItem)
                PageBase.NavMain.SelectedItem = PageBase.NavItemBlank;
            previousNavigationItem = PageBase.NavMain.SelectedItem;
        }

        public static void NavigateRefresh()
        {
            var peek = NavigationHistory.Peek();
            BaseFrame.Navigate(peek.PageType, peek.Paratmers);
            GC.Collect();
        }

        public static async Task NavigatePageResource(string resourceId)
        {
            switch (resourceId.Substring(0, 2))
            {
                case "al":
                    NavigatePage(typeof(AlbumPage), resourceId.Substring(2));
                    break;
                case "pl":
                    NavigatePage(typeof(SongListDetail), resourceId.Substring(2));
                    break;
                case "rd":
                    NavigatePage(typeof(RadioPage), resourceId.Substring(2));
                    break;
                case "ar":
                    NavigatePage(typeof(ArtistPage), resourceId.Substring(2));
                    break;
                case "us":
                    NavigatePage(typeof(Me), resourceId.Substring(2));
                    break;
                case "ns":
                    await HyPlayList.AppendNcSource(resourceId);
                    HyPlayList.SongAppendDone();
                    HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => "ns" + t.PlayItem.Id == resourceId));
                    break;
                case "ml":
                    NavigatePage(typeof(MVPage), resourceId.Substring(2));
                    break;
            }
        }

        public static void CollectGarbage()
        {
            NavigatePage(typeof(BlankPage));
            BaseFrame.Content = null;
            PageExpandedPlayer?.Dispose();
            PageExpandedPlayer = null;
            PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
            _ = ImageCache.Instance.ClearAsync();
            KawazuConv?.Dispose();
            KawazuConv = null;
        }

        public static void UIElement_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = sender as UIElement;
            try
            {
                element?.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
            }
            catch
            {
                var flyout = FlyoutBase.GetAttachedFlyout((FrameworkElement)element!);
                flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
            }
        }

        public static void NavigateBack()
        {
            if (Setting.forceMemoryGarbage)
            {
                if (NavigationHistory.Count > 1)
                    NavigationHistory.Pop();
                try
                {
                    var bak = NavigationHistory.Peek();
                    while (bak.PageType == typeof(BlankPage))
                    {
                        NavigationHistory.Pop();
                        bak = NavigationHistory.Peek();
                    }

                    BaseFrame?.Navigate(bak.PageType, bak.Paratmers,
                        new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
                    NavigatingBack = true;
                    PageBase.NavMain.SelectedItem = bak.Item;
                    NavigatingBack = false;
                    GC.Collect();
                }
                catch
                {
                }
            }
            else
            {
                if (BaseFrame != null && BaseFrame.CanGoBack)
                    BaseFrame?.GoBack();
            }
        }

        public class NavigationHistoryItem
        {
            public object Item;
            public Type PageType;
            public object Paratmers;
        }
    }

    internal class ColorHelper
    {
        public static Color GetReversedColor(Color color)
        {
            var grayLevel = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            if (grayLevel > 0.1)
                return Colors.Black;
            return Colors.White;
        }
    }

    internal class Setting : INotifyPropertyChanged
    {
        public int lyricSize
        {
            get
            {
                var ret = 0;
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("lyricSize"))
                    if (int.TryParse(ApplicationData.Current.LocalSettings.Values["lyricSize"].ToString(), out ret))
                        return ret;
                return ret;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["lyricSize"] = value;
                OnPropertyChanged();
            }
        }

        public bool hotlyricOnStartup
        {
            get => GetSettings("hotlyricOnStartup", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["hotlyricOnStartup"] = value;
                OnPropertyChanged();
            }
        }

        public bool playbarButtonsTransparent
        {
            get => GetSettings("playbarButtonsTransparent", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["playbarButtonsTransparent"] = value;
                OnPropertyChanged();
            }
        }

        public bool playbarBackgroundElay
        {
            get => GetSettings("playbarBackgroundElay", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["playbarBackgroundElay"] = value;
                OnPropertyChanged();
            }
        }

        public bool playButtonAccentColor
        {
            get => GetSettings("playButtonAccentColor", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["playButtonAccentColor"] = value;
                OnPropertyChanged();
            }
        }

        public int expandedPlayerBackgroundType
        {
            get => GetSettings("expandedPlayerBackgroundType", 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values["expandedPlayerBackgroundType"] = value;
                OnPropertyChanged();
            }
        }

        public bool TintOpacityValue
        {
            get => GetSettings("TintOpacityValue", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["TintOpacityValue"] = value;
                OnPropertyChanged();
            }
        }

        public bool downloadLyric
        {
            get => GetSettings("downloadLyric", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["downloadLyric"] = value;
                OnPropertyChanged();
            }
        }

        public bool downloadTranslation
        {
            get => GetSettings("downloadTranslation", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["downloadTranslation"] = value;
                OnPropertyChanged();
            }
        }

        public bool usingGBK
        {
            get => GetSettings("usingGBK", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["usingGBK"] = value;
                OnPropertyChanged();
            }
        }

        public bool writedownloadFileInfo
        {
            get => GetSettings("writedownloadFileInfo", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["writedownloadFileInfo"] = value;
                OnPropertyChanged();
            }
        }

        public bool write163Info
        {
            get => GetSettings("write163Info", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["write163Info"] = value;
                OnPropertyChanged();
            }
        }

        public bool displayShuffledList
        {
            get => GetSettings("displayShuffledList", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["displayShuffledList"] = value;
                OnPropertyChanged();
            }
        }

        public bool doScrobble
        {
            get => GetSettings("doScrobble", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["doScrobble"] = value;
                OnPropertyChanged();
            }
        }

        public bool localProgressiveLoad
        {
            get => GetSettings("localProgressiveLoad", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["localProgressiveLoad"] = value;
                OnPropertyChanged();
            }
        }

        public bool shuffleNoRepeating
        {
            get => GetSettings("shuffleNoRepeating", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["shuffleNoRepeating"] = value;
                OnPropertyChanged();
                if (HyPlayList.NowPlayType == PlayMode.Shuffled && value) HyPlayList.CreateShufflePlayLists();
            }
        }

        public int lyricScaleSize
        {
            get => GetSettings("lyricScaleSize", 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values["lyricScaleSize"] = value;
                OnPropertyChanged();
            }
        }

        public bool forceMemoryGarbage
        {
            get => GetSettings("forceMemoryGarbage", false);
            set => ApplicationData.Current.LocalSettings.Values["forceMemoryGarbage"] = value;
        }

        public bool expandedUseAcrylic
        {
            get => GetSettings("expandedUseAcrylic", true);
            set => ApplicationData.Current.LocalSettings.Values["expandedUseAcrylic"] = value;
        }

        public bool playbarBackgroundBreath
        {
            get => GetSettings("playbarBackgroundBreath", false);
            set => ApplicationData.Current.LocalSettings.Values["playbarBackgroundBreath"] = value;
        }

        public bool playbarBackgroundAcrylic
        {
            get => GetSettings("playbarBackgroundAcrylic", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["playbarBackgroundAcrylic"] = value;
                OnPropertyChanged();
            }
        }

        public bool expandAlbumBreath
        {
            get => GetSettings("expandAlbumBreath", false);
            set => ApplicationData.Current.LocalSettings.Values["expandAlbumBreath"] = value;
        }

        public bool listHeaderAcrylicBlur
        {
            get => GetSettings("listHeaderAcrylicBlur", true);
            set => ApplicationData.Current.LocalSettings.Values["listHeaderAcrylicBlur"] = value;
        }

        public bool itemOfListBackgroundAcrylicBlur
        {
            get => GetSettings("itemOfListBackgroundAcrylicBlur", false);
            set => ApplicationData.Current.LocalSettings.Values["itemOfListBackgroundAcrylicBlur"] = value;
        }

        public bool lyricDropshadow
        {
            get => GetSettings("lyricDropshadow", false);
            set => ApplicationData.Current.LocalSettings.Values["lyricDropshadow"] = value;
        }

        public bool safeFileAccess
        {
            get => GetSettings("safeFileAccess", false);
            set => ApplicationData.Current.LocalSettings.Values["safeFileAccess"] = value;
        }

        public List<string> scanLocalFolder
        {
            get
            {
                var folders = GetSettings("scanLocalFolder", KnownFolders.MusicLibrary.Path);
                return folders.Split("\r\n").ToList();
            }
            set => ApplicationData.Current.LocalSettings.Values["safeFileAccess"] = string.Join("\r\n", value);
        }

        public int lyricColor
        {
            get => GetSettings("lyricColor", 0);
            set => ApplicationData.Current.LocalSettings.Values["lyricColor"] = value;
        }

        public int downloadNameOccupySolution
        {
            get => GetSettings("downloadNameOccupySolution", 0);
            set => ApplicationData.Current.LocalSettings.Values["downloadNameOccupySolution"] = value;
        }


        public bool albumRotate
        {
            get => GetSettings("albumRotate", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["albumRotate"] = value;
                if (value) albumRound = true;
                OnPropertyChanged();
            }
        }

        public bool albumRound
        {
            get => GetSettings("albumRound", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["albumRound"] = value;
                if (!value) albumRotate = false;
                OnPropertyChanged();
            }
        }

        public int albumBorderLength
        {
            get => GetSettings("albumBorderLength", 0);
            set => ApplicationData.Current.LocalSettings.Values["albumBorderLength"] = value;
        }

        public int romajiSize
        {
            get => GetSettings("romajiSize", 15);
            set
            {
                ApplicationData.Current.LocalSettings.Values["romajiSize"] = value;
                OnPropertyChanged();
            }
        }

        public bool noImage
        {
            get => GetSettings("noImage", false);
            set => ApplicationData.Current.LocalSettings.Values["noImage"] = value;
        }

        public bool lyricAlignment
        {
            get => GetSettings("lyricAlignment", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["lyricAlignment"] = value;
                OnPropertyChanged();
            }
        }

        public bool ancientSMTC
        {
            get => GetSettings("ancientSMTC", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["ancientSMTC"] = value;
                OnPropertyChanged();
            }
        }

        public bool jumpVipSongPlaying
        {
            get => GetSettings("jumpVipSongPlaying", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["jumpVipSongPlaying"] = value;
                OnPropertyChanged();
            }
        }

        public bool jumpVipSongDownloading
        {
            get => GetSettings("jumpVipSongDownloading", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["jumpVipSongDownloading"] = value;
                OnPropertyChanged();
            }
        }

        public string audioRate
        {
            get => GetSettings("audioRate", "exhigh");
            set
            {
                ApplicationData.Current.LocalSettings.Values["audioRate"] = value;
                OnPropertyChanged();
            }
        }

        public string downloadAudioRate
        {
            get => GetSettings("downloadAudioRate", "hires");
            set
            {
                ApplicationData.Current.LocalSettings.Values["downloadAudioRate"] = value;
                OnPropertyChanged();
            }
        }

        public bool xboxHidePointer
        {
            get => GetSettings("xboxHidePointer", false);
            set => ApplicationData.Current.LocalSettings.Values["xboxHidePointer"] = value;
        }

        public bool enableTouchGestureAction
        {
            get => GetSettings("enableTouchGestureAction", false);
            set => ApplicationData.Current.LocalSettings.Values["enableTouchGestureAction"] = value;
        }

        public int gestureMode
        {
            get => GetSettings("gestureMode", 0);
            set => ApplicationData.Current.LocalSettings.Values["gestureMode"] = value;
        }

        public int maxDownloadCount
        {
            get => GetSettings("maxDownloadCount", 1);
            set => ApplicationData.Current.LocalSettings.Values["maxDownloadCount"] = value;
        }

        public int Volume
        {
            get
            {
                try
                {
                    return GetSettings("Volume", 50);
                }
                catch
                {
                    return 50;
                }
            }

            set => ApplicationData.Current.LocalSettings.Values["Volume"] = value;
        }

        public string downloadDir
        {
            get
            {
                try
                {
                    return GetSettings("downloadDir", KnownFolders.MusicLibrary
                        .CreateFolderAsync("HyPlayer", CreationCollisionOption.OpenIfExists).AsTask().Result.Path);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["downloadDir"] = value;
                OnPropertyChanged();
            }
        }

        public string downloadFileName
        {
            get => GetSettings("downloadFileName", "{$SINGER} - {$SONGNAME}");
            set
            {
                ApplicationData.Current.LocalSettings.Values["downloadFileName"] = value;
                OnPropertyChanged();
            }
        }

        public string searchingDir
        {
            get
            {
                try
                {
                    return GetSettings("searchingDir", downloadDir);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["searchingDir"] = value;
                OnPropertyChanged();
            }
        }

        public string cacheDir
        {
            get
            {
                try
                {
                    return GetSettings("cacheDir", ApplicationData.Current.LocalCacheFolder
                        .CreateFolderAsync("songCache", CreationCollisionOption.OpenIfExists).AsTask().GetAwaiter()
                        .GetResult().Path);
                }
                catch
                {
                    return ApplicationData.Current.LocalCacheFolder.Path;
                }
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["cacheDir"] = value;
                OnPropertyChanged();
            }
        }

        public bool fadeInOut
        {
            get => GetSettings("FadeInOut", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["FadeInOut"] = value;
                OnPropertyChanged();
            }
        }

        public bool fadeInOutPause
        {
            get => GetSettings("FadeInOutPause", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["FadeInOutPause"] = value;
                OnPropertyChanged();
            }
        }

        public bool notClearMode
        {
            get => GetSettings("notClearMode", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["notClearMode"] = value;
                OnPropertyChanged();
            }
        }

        public bool useTaglibPicture
        {
            get => GetSettings("useTaglibPicture", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["useTaglibPicture"] = value;
                OnPropertyChanged();
            }
        }

        public bool showComposerInLyric
        {
            get => GetSettings("showComposerInLyric", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["showComposerInLyric"] = value;
                OnPropertyChanged();
            }
        }

        public bool advancedMusicHistoryStorage
        {
            get => GetSettings("advancedMusicHistoryStorage", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["advancedMusicHistoryStorage"] = value;
                OnPropertyChanged();
            }
        }

        public double fadeInOutTime
        {
            get
            {
                try
                {
                    return GetSettings<double>("fadeInOutTime", 3);
                }
                catch
                {
                    return 3;
                }
            }

            set => ApplicationData.Current.LocalSettings.Values["fadeInOutTime"] = value;
        }

        public double fadeInOutTimePause
        {
            get
            {
                try
                {
                    return GetSettings<double>("fadeInOutTimePause", 3);
                }
                catch
                {
                    return 3;
                }
            }

            set => ApplicationData.Current.LocalSettings.Values["fadeInOutTimePause"] = value;
        }

        public bool playBarMargin
        {
            get => GetSettings("playBarMargin", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["playBarMargin"] = value;
                OnPropertyChanged();
            }
        }

        public bool noUseHotLyric
        {
            get => GetSettings("noUseHotLyric", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["noUseHotLyric"] = value;
                OnPropertyChanged();
            }
        }

        public bool toastLyric
        {
            get => GetSettings("toastLyric", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["toastLyric"] = value;
                OnPropertyChanged();
            }
        }

        public bool expandAnimation
        {
            get => GetSettings("expandAnimation", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["expandAnimation"] = value ? "true" : "false";
                OnPropertyChanged();
            }
        }

        public bool uiSound
        {
            get => GetSettings("uiSound", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["uiSound"] = value;
                OnPropertyChanged();
            }
        }

        public int songRollType
        {
            get => GetSettings("songRollType", 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values["songRollType"] = value;
                OnPropertyChanged();
            }
        }

        public bool songUrlLazyGet
        {
            get => GetSettings("songUrlLazyGet", true);
            set
            {
                ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] = value;
                OnPropertyChanged();
            }
        }

        public bool enableCache
        {
            get => GetSettings("enableCache", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["enableCache"] = value;
                OnPropertyChanged();
            }
        }

        public bool highQualityCoverInSMTC
        {
            get => GetSettings("highQualityCoverInSMTC", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["highQualityCoverInSMTC"] = value;
                OnPropertyChanged();
            }
        }

        public bool progressInSMTC
        {
            get => GetSettings("progressInSMTC", true);
            set
            {
                if (value)
                {
                    HyPlayList.MediaSystemControls.PlaybackPositionChangeRequested +=
                        HyPlayList.MediaSystemControls_PlaybackPositionChangeRequested;
                    HyPlayList.Player.PlaybackSession.PositionChanged += HyPlayList.UpdateSmtcPosition;
                }
                else
                {
                    HyPlayList.MediaSystemControls.PlaybackPositionChangeRequested -=
                        HyPlayList.MediaSystemControls_PlaybackPositionChangeRequested;
                    HyPlayList.Player.PlaybackSession.PositionChanged -= HyPlayList.UpdateSmtcPosition;
                }

                ApplicationData.Current.LocalSettings.Values["progressInSMTC"] = value;
                OnPropertyChanged();
            }
        }

        public bool expandedPlayerFullCover
        {
            get => GetSettings("expandedPlayerFullCover", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["expandedPlayerFullCover"] = value;
                OnPropertyChanged();
            }
        }

        public int themeRequest
        {
            // 0 - 未设置   1 - 浅色  2 - 深色
            get => GetSettings("themeRequest", 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values["themeRequest"] = value;
                OnPropertyChanged();
            }
        }

        public int expandedCoverShadowDepth
        {
            get => GetSettings("expandedCoverShadowDepth", 4);
            set
            {
                ApplicationData.Current.LocalSettings.Values["expandedCoverShadowDepth"] = value;
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
            get => GetSettings("DisablePopUp", false);
            set => ApplicationData.Current.LocalSettings.Values["DisablePopUp"] = value;
        }

        public int UpdateSource
        {
            get => GetSettings("UpdateSource", 1);
            set
            {
                ApplicationData.Current.LocalSettings.Values["UpdateSource"] = value;
                OnPropertyChanged();
            }
        }

        public bool enableTile
        {
            get => GetSettings("enableTile", Environment.OSVersion.Version.Build < 22000);
            set
            {
                ApplicationData.Current.LocalSettings.Values["enableTile"] = value;
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
            get => GetSettings("canaryChannelAvailability", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["canaryChannelAvailability"] = value;
                OnPropertyChanged();
            }
        }

        public bool tileBackgroundAvailability
        {
            get => GetSettings("tileBackgroundAvailability", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["tileBackgroundAvailability"] = value;
                OnPropertyChanged();
            }
        }

        public bool saveTileBackgroundToLocalFolder
        {
            get => GetSettings("saveTileBackgroundToLocalFolder", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["saveTileBackgroundToLocalFolder"] = value;
                OnPropertyChanged();
            }
        }

        public TimeSpan ABStartPoint
        {
            get => _abStartPoint;
            set
            {
                _abStartPoint = value;
                OnPropertyChanged("ABStartPointFriendlyValue");
            }
        }

        public string ABStartPointFriendlyValue =>
            ABStartPoint.Hours + ":"
                               + ABStartPoint.Minutes + ":"
                               + ABStartPoint.Seconds;

        private TimeSpan _abStartPoint = TimeSpan.Zero;

        public TimeSpan ABEndPoint
        {
            get => _abEndPoint;
            set
            {
                _abEndPoint = value;
                OnPropertyChanged("ABEndPointFriendlyValue");
            }
        }

        private TimeSpan _abEndPoint = TimeSpan.Zero;

        public string ABEndPointFriendlyValue =>
            ABEndPoint.Hours + ":"
                             + ABEndPoint.Minutes + ":"
                             + ABEndPoint.Seconds;

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

        public bool ForceAcrylicBackground
        {
            get => GetSettings("forceAcrylicBackground", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["forceAcrylicBackground"] = value;
                OnPropertyChanged();
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
        
        public bool UseLastFMScrobbler
        {
            get => GetSettings("useLastFMScrobbler", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["useLastFMScrobbler"] = value;
                OnPropertyChanged();
            }
        }

        public bool UpdateLastFMNowPlaying
        {
            get => GetSettings("updateLastFMNowPlaying", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["updateLastFMNowPlaying"] = value;
                OnPropertyChanged();
            }
        }

        public string LastFMUserName
        {
            get => GetSettings("lastFMUserName", string.Empty);
            set
            {
                ApplicationData.Current.LocalSettings.Values["lastFMUserName"] = value;
                OnPropertyChanged();
            }
        }

        public string LastFMToken
        {
            get => GetSettings("lastFMToken", string.Empty);
            set
            {
                ApplicationData.Current.LocalSettings.Values["lastFMToken"] = value;
                OnPropertyChanged();
                OnPropertyChanged("LastFMLogined");
            }
        }

        public bool LastFMIsSubscriber
        {
            get => GetSettings("lastFMIsSubscriber", false);
            set
            {
                ApplicationData.Current.LocalSettings.Values["lastFMisSubscriber"] = value;
                OnPropertyChanged();
            }
        }

        public bool LastFMLogined => LastFMManager.LastfmLogined;

        public event PropertyChangedEventHandler? PropertyChanged;

        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
        }

        public static T GetSettings<T>(string propertyName, T defaultValue)
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey(propertyName) &&
                    ApplicationData.Current.LocalSettings.Values[propertyName] != null &&
                    !string.IsNullOrEmpty(ApplicationData.Current.LocalSettings.Values[propertyName].ToString()))
                {
                    if (typeof(T).ToString() == "System.Boolean")
                        return (T)(object)bool.Parse(ApplicationData.Current.LocalSettings.Values[propertyName]
                            .ToString());

                    //超长的IF
                    return (T)ApplicationData.Current.LocalSettings.Values[propertyName];
                }

                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    internal class HistoryManagement
    {
        public static void InitializeHistoryTrack()
        {
            var list = new List<string>();
            if (ApplicationData.Current.LocalSettings.Values["songHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["searchHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonConvert.SerializeObject(list);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddNCSongHistory(string songid)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songHistory"].ToString());

            list.Remove(songid);
            list.Insert(0, songid);
            if (list.Count >= 300)
                list.RemoveRange(9, list.Count - 300);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddSearchHistory(string Text)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString());
            if (!list.Contains(Text))
            {
                list.Insert(0, Text);
            }
            else
            {
                list.RemoveAll(t => t == Text);
                list.Insert(0, Text);
            }

            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
        }

        public static void AddSonglistHistory(string playListid)
        {
            var list = new List<string>();
            list = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songlistHistory"].ToString());

            list.Remove(playListid);
            list.Insert(0, playListid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
        }

        public static async Task SetcurPlayingListHistory(List<string> songids)
        {
            if (Common.Setting.advancedMusicHistoryStorage)
                try
                {
                    var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                        CreationCollisionOption.OpenIfExists);
                    await FileIO.WriteTextAsync(file, string.Join("\r\n", songids));
                }
                catch
                {
                    // ignored
                }
            else
                //低级音乐存储
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonConvert.SerializeObject(songids.Count > 100 ? songids.GetRange(0, 100) : songids);
        }

        public static async Task ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
            await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                CreationCollisionOption.OpenIfExists)).DeleteAsync();
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            var retsongs = new List<NCSong>();
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                    new Dictionary<string, object>
                    {
                        ["ids"] = string.Join(",",
                            JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                                .Values["songHistory"].ToString()))
                    });
                return json["songs"].ToArray().Select(t => NCSong.CreateFromJson(t)).ToList();
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }

            return new List<NCSong>();
        }

        public static async Task<List<NCPlayList>> GetSonglistHistory()
        {
            var i = 0;
            var queries = new Dictionary<string, object>();
            foreach (var plid in JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                         .Values["songlistHistory"].ToString()))
                queries["/api/v6/playlist/detail" + new string('/', i++)] = JsonConvert.SerializeObject(
                    new Dictionary<string, object>
                    {
                        ["id"] = plid,
                        ["n"] = 100000,
                        ["s"] = 8
                    });
            if (queries.Count == 0) return new List<NCPlayList>();
            var ret = new List<NCPlayList>();
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Batch, queries);

                for (var k = 0; k < json.Count - 1; k++)
                    ret.Add(NCPlayList.CreateFromJson(
                        json["/api/v6/playlist/detail" + new string('/', k)]["playlist"]));
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }

            return ret;
        }

        public static List<string> GetSearchHistory()
        {
            return JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString());
        }

        public static async Task<List<NCSong>> GetcurPlayingListHistory()
        {
            var retsongs = new List<NCSong>();
            List<string> trackIds = new();
            if (Common.Setting.advancedMusicHistoryStorage)
                trackIds = (await FileIO.ReadTextAsync(
                    await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                        CreationCollisionOption.OpenIfExists))).Split("\r\n").ToList();
            else
                //低级音乐存储
                trackIds = JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["curPlayingListHistory"].ToString()) ?? new List<string>();

            if (trackIds == null || trackIds.Count == 0)
                return retsongs;
            var nowIndex = 0;
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object> { ["ids"] = string.Join(",", nowIds) });
                    nowIndex++;
                    var i = 0;
                    var ncSongs = (json["songs"] ?? new JArray()).Select(t =>
                    {
                        if (json["privileges"] == null) return null;
                        if (json["privileges"].ToList()[i++]["st"]?.ToString() == "0")
                            return NCSong.CreateFromJson(t);

                        return null;
                    }).ToList();
                    ncSongs.RemoveAll(t => t == null);
                    retsongs.AddRange(ncSongs);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }
            }

            return retsongs;
        }
    }

    internal static class Extensions
    {
        public static byte[] ToByteArrayUtf8(this string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        public static string ToHexStringLower(this byte[] value)
        {
            var sb = new StringBuilder();
            foreach (var t in value) sb.Append(t.ToString("x2"));

            return sb.ToString();
        }

        public static string ToHexStringUpper(this byte[] value)
        {
            var sb = new StringBuilder();
            foreach (var t in value) sb.Append(t.ToString("X2"));

            return sb.ToString();
        }

        public static string ToBase64String(this byte[] value)
        {
            return Convert.ToBase64String(value);
        }

        private static MD5? _md5;

        public static byte[] ComputeMd5(this byte[] value)
        {
            _md5 ??= MD5.Create();
            return _md5.ComputeHash(value);
        }

        public static byte[] RandomBytes(this Random random, int length)
        {
            var buffer = new byte[length];
            random.NextBytes(buffer);
            return buffer;
        }

        public static string Get(this CookieCollection cookies, string name, string defaultValue)
        {
            return cookies[name]?.Value ?? defaultValue;
        }
    }
}