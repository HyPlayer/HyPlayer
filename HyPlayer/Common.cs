#region

#nullable enable
using CommunityToolkit.Mvvm.DependencyInjection;
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
        public static HttpClient? HttpClient;
        public static NeteaseCloudMusicApiHandler? NeteaseAPI;
        public static LastFMClient? LastFMClient;
        public static XboxGameBarWidget? XboxGameBarWidget;
        public static PixelShaderEffect? PixelShaderShareEffect;
        public static Setting? Setting;
#nullable restore
        public static BrushManagement BrushManagement = new();
        
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
            Setting = Ioc.Default.GetRequiredService<Setting>();
            HttpClient = Ioc.Default.GetRequiredService<HttpClient>();
            NeteaseAPI = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
            LastFMClient = Ioc.Default.GetRequiredService<LastFMClient>();
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
        public static int PlaybarSecondSetting => Setting.AutoHidePlaybarTime;
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
                    HyPlayList.SongMoveTo(HyPlayList.List.Find(t => "ns" + t.PlayItem.Id == resourceId));
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