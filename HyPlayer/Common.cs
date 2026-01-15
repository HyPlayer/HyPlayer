#region

#nullable enable
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Pages;
using Kawazu;
using LiteFM;
using LiteFM.Abstractions;
using Microsoft.Gaming.XboxGameBar;



//using Microsoft.Gaming.XboxGameBar;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Toolkit.Uwp.UI;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Color = Windows.UI.Color;
using HttpClient = System.Net.Http.HttpClient;
#if !DEBUG
#endif

#endregion

namespace HyPlayer
{
    internal static class Common
    {
        public delegate void EnterForegroundFromBackgroundEvent();
        public delegate void PlaybarVisibilityChangedEvent(bool isActivated);

        public static bool Logined = false;
        public static bool IsInFm = false;
        public static bool IsInBackground = false;
#nullable enable
        public static NCUser? LoginedUser;
        public static ExpandedPlayer? PageExpandedPlayer;
        public static MainPage? PageMain;
        public static PlayBar? BarPlayBar;
        public static Frame? BaseFrame;
        public static BasePage? PageBase;
        public static KawazuConverter? KawazuConv;
        public static HttpClientHandler? HttpClientHandler;
        public static HttpClient? HttpClient;
        public static NeteaseCloudMusicApiHandler? NeteaseAPI;
        public static LastFMClient? LastFMClient;
        public static XboxGameBarWidget? XboxGameBarWidget;
        public static PixelShaderEffect? PixelShaderShareEffect;
#nullable restore
        public static BrushManagement BrushManagement = new();
        public static Setting Setting = new();
        public static bool ShowLyricSound = true;
        public static bool ShowLyricTrans = true;
        public static List<string> LikedSongs = new();
        public static List<NCPlayList> MySongLists = new();
        public static DisplayRequest DisplayRequest = new();
        public static readonly Stack<NavigationHistoryItem> NavigationHistory = new();
        public static JsonSerializerOptions DefaultOptions = new()
        {
            TypeInfoResolver = JsonDefaultContext.Default
        };

        public static void InitializeHttpClientAndAPI()
        {
            HttpClientHandler = NeteaseCloudMusicApiHandler.HttpClientHandler;
            HttpClientHandler.UseProxy = Setting.EnableProxy;

            HttpClient = new HttpClient(HttpClientHandler);
            NeteaseAPI = Locator.Instance.GetService<NeteaseCloudMusicApiHandler>();
            LastFMClient = Locator.Instance.GetService<LastFMClient>();
            NeteaseAPI.Option.AdditionalParameters = Setting.ApiAdditionalParameters;
            NeteaseAPI.Option.FakeCheckToken = Setting.EnableCheckTokenApi;
        }
        public static bool isExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                Setting.OnPropertyChanged("playbarBackgroundAcrylic");
            }
        }

        private static bool _isExpanded = false;
#nullable enable
        public static TeachingTip? GlobalTip;
        public static EnterForegroundFromBackgroundEvent? OnEnterForegroundFromBackground;
        public static PlaybarVisibilityChangedEvent? OnPlaybarVisibilityChanged;
        public static readonly Queue<KeyValuePair<string, string?>> TeachingTipList = new();
#nullable restore
        public static List<string> ErrorMessageList = new();
        public static ObservableCollection<string> Logs = new();
        public static bool NavigatingBack;
        private static int _teachingTipSecondCounter = 3;
        public static int PlaybarSecondCounter = 0;
        public static int PlaybarSecondSetting = Setting.AutoHidePlaybarTime;
        public static bool PlaybarIsVisible = true;
        public static bool PointerIsInMainPage = false;

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
                catch
                {
                    //Ignore
                }
            return null;
        }

#nullable enable
        public static void AddToTeachingTipLists(string title, string? subtitle = "")
        {
            TeachingTipList.Enqueue(new KeyValuePair<string, string?>(title, subtitle));
            _ = Invoke(() =>
            {
                if (GlobalTip != null)
                {
                    if (!GlobalTip.IsOpen)
                        RollTeachingTip(false);
                }
            });
        }
#nullable restore

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
                var (title, subtitle) = TeachingTipList.Dequeue(); // deconstruction
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

        public static void ChangePlaybarVisibillity()
        {
            if (PointerIsInMainPage)
            {
                PlaybarSecondCounter = 0;
                return;
            }
            if (++PlaybarSecondCounter >= PlaybarSecondSetting)
            {
                if (PlaybarIsVisible)
                {
                    OnPlaybarVisibilityChanged?.Invoke(false);
                    PlaybarIsVisible = false;
                }
            }
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
            /*
            if (previousNavigationItem == PageBase.NavMain.SelectedItem)
                PageBase.NavMain.SelectedItem = PageBase.NavItemBlank;
            previousNavigationItem = PageBase.NavMain.SelectedItem;
            */
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
                    /*
                    PageBase.NavMain.SelectedItem = bak.Item;
                    */
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

        public static Color FromHsv(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2f - 1f));
            float m = v - c;

            float r = 0, g = 0, b = 0;

            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(
                255,
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255)
            );
        }
    }

    internal class Setting : INotifyPropertyChanged
    {
        public int ColorGeneratorType
        {
            get => GetSettings(nameof(ColorGeneratorType), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(ColorGeneratorType)] = value;
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
                if(value == null) 
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
                        return GetSettings<double>(nameof(CustomTintOpacity), 3d);
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

        public int PerformanceMode
        {
            get => GetSettings(nameof(PerformanceMode), 1);
            set => ApplicationData.Current.LocalSettings.Values[nameof(PerformanceMode)] = value;
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
                if (Common.NeteaseAPI != null) Common.NeteaseAPI.Option.FakeCheckToken = value;
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
                return folders.Split("\r\n").ToList();
            }
            set => ApplicationData.Current.LocalSettings.Values[nameof(safeFileAccess)] = string.Join("\r\n", value);
        }

        public int lyricColor
        {
            get => GetSettings(nameof(lyricColor), 0);
            set => ApplicationData.Current.LocalSettings.Values[nameof(lyricColor)] = value;
        }

        public int downloadNameOccupySolution
        {
            get => GetSettings(nameof(downloadNameOccupySolution), 0);
            set => ApplicationData.Current.LocalSettings.Values[nameof(downloadNameOccupySolution)] = value;
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

        public int lyricAlignment
        {
            get => GetSettings(nameof(lyricAlignment), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(lyricAlignment)] = value;
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
            get => GetSettings(nameof(lyricRenderSimpleLineScanning), true);
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

        public int gestureMode
        {
            get => GetSettings(nameof(gestureMode), 0);
            set => ApplicationData.Current.LocalSettings.Values[nameof(gestureMode)] = value;
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
            get => GetSettings("CrossFade", false);
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
                Common.PlaybarSecondSetting = value;
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
            get
            {
                try
                {
                    if (CrossFade)
                    {
                        return GetSettings<double>(nameof(CrossFadeTime), 3d);
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

        public int songRollType
        {
            get => GetSettings(nameof(songRollType), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(songRollType)] = value;
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
            get => GetSettings(nameof(enableCache), false);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(enableCache)] = value;
                OnPropertyChanged();
            }
        }

        public bool enableApiCache
        {
            get => GetSettings(nameof(enableApiCache), true);
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

        public int themeRequest
        {
            // 0 - 未设置   1 - 浅色  2 - 深色
            get => GetSettings(nameof(themeRequest), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(themeRequest)] = value;
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

        public int UpdateSource
        {
            get => GetSettings(nameof(UpdateSource), 1);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(UpdateSource)] = value;
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
                OnPropertyChanged(nameof(ABEndPointFriendlyValue));
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

        public int LineRollingCalculator
        {
            get => GetSettings(nameof(LineRollingCalculator), 0);
            set
            {
                ApplicationData.Current.LocalSettings.Values[nameof(LineRollingCalculator)] = value;
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
                        HyPlayList.Player.SetPlaybackSourceOutputVolume(HyPlayList.NowPlayingItem?.PlayItem.Volume ?? 1, HyPlayList.Player.PrimaryPlaybackSource);
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
            get => GetSettings(nameof(IsolationFullThrottle), false);
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
            get => GetSettings(nameof(IsolationLightWave), true);
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

        public bool SaveCookies()
        {
            var container = ApplicationData.Current.LocalSettings.CreateContainer("LoginedUser", ApplicationDataCreateDisposition.Always);
            container.Values.Clear();
            foreach (var item in Common.NeteaseAPI.Option.Cookies)
            {
                container.Values[item.Key] = item.Value;
            }
            return true;
        }
        public bool LoadCookies()
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
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
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
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["searchHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] == null)
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] =
                    JsonSerializer.Serialize(list, Common.DefaultOptions);
            if (ApplicationData.Current.LocalSettings.Values["songlistHistory"].ToString().StartsWith("[{"))
                ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddNCSongHistory(string songid)
        {
            var list = new List<string>();
            list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songHistory"].ToString(), Common.DefaultOptions);

            list.Remove(songid);
            list.Insert(0, songid);
            if (list.Count >= 300)
                list.RemoveRange(9, list.Count - 300);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddSearchHistory(string Text)
        {
            var list = new List<string>();
            list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), Common.DefaultOptions);
            if (!list.Contains(Text))
            {
                list.Insert(0, Text);
            }
            else
            {
                list.RemoveAll(t => t == Text);
                list.Insert(0, Text);
            }

            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
        }

        public static void AddSonglistHistory(string playListid)
        {
            var list = new List<string>();
            list = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["songlistHistory"].ToString(), Common.DefaultOptions);

            list.Remove(playListid);
            list.Insert(0, playListid);
            if (list.Count >= 100)
                list.RemoveRange(100, list.Count - 100);
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
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
                    JsonSerializer.Serialize(songids.Count > 100 ? songids.GetRange(0, 100) : songids, Common.DefaultOptions);
        }

        public static async Task ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonSerializer.Serialize(list, Common.DefaultOptions);
            await (await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("songPlayHistory",
                CreationCollisionOption.OpenIfExists)).DeleteAsync();
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            try
            {
                var songIds = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["songHistory"].ToString(), Common.DefaultOptions);
                var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongDetailApi,
                    new SongDetailRequest()
                    {
                        IdList = songIds
                    });
                if (result.IsSuccess)
                {
                    return result.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
                }
            }
            catch (Exception e)
            {
                Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            }

            return [];
        }

        public static List<string> GetSearchHistory()
        {
            return JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString(), Common.DefaultOptions);
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
                trackIds = JsonSerializer.Deserialize<List<string>>(ApplicationData.Current.LocalSettings
                    .Values["curPlayingListHistory"].ToString(), Common.DefaultOptions) ?? new List<string>();

            if (trackIds == null || string.IsNullOrEmpty(trackIds.FirstOrDefault()))
                return retsongs;
            var nowIndex = 0;
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    var json = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.SongDetailApi,
                        new SongDetailRequest()
                        {
                            IdList = nowIds
                        });
                    nowIndex++;
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("加载当前播放失败", json.Error.Message);
                        continue;
                    }

                    var ncSongs = json.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
                    retsongs.AddRange(ncSongs ?? []);
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

    public enum RomajiSource : int
    {
        None,
        AutoSelect,
        NeteaseOnly,
        KawazuOnly
    }

    public enum BackgroundType : int
    {
        CoverBlur = 0,
        CoverTheme = 1,
        DesktopAcrylic = 5,
        Animated = 6,
        Isolation = 7
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
#nullable enable
        private static MD5? _md5;
#nullable restore
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