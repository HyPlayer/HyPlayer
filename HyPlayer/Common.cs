using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.Pages;
using Kawazu;
using NeteaseCloudMusicApi;
using Newtonsoft.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;

namespace HyPlayer
{
    internal class Common
    {
        public static CloudMusicApi ncapi = new CloudMusicApi();
        public static bool Logined = false;
        public static NCUser LoginedUser;
        public static ExpandedPlayer PageExpandedPlayer;
        public static MainPage PageMain;
        public static PlayBar BarPlayBar;
        public static Frame BaseFrame;
        public static BasePage PageBase;
        public static Setting Setting = new Setting();
        public static bool ShowLyricSound = true;
        public static bool ShowLyricTrans = true;
        public static Dictionary<string, object> GLOBAL = new Dictionary<string, object>();
        public static List<string> LikedSongs = new List<string>();
        public static KawazuConverter KawazuConv = null;
        public static List<NCPlayList> MySongLists = new List<NCPlayList>();
        public static List<NCSong> ListedSongs = new List<NCSong>();
        public static readonly Stack<NavigationHistoryItem> NavigationHistory = new Stack<NavigationHistoryItem>();
        public static bool isExpanded = false;

        public struct NavigationHistoryItem
        {
            public Type PageType;
            public object Paratmers;
            public object Item;
        }

        public static async void Invoke(Action action, CoreDispatcherPriority Priority = CoreDispatcherPriority.Normal)
        {
            try
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority,
                    () => { action(); });
            }
            catch (Exception e)
            {
#if RELEASE
                Crashes.TrackError(e);
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
        }

        public static void NavigatePage(Type SourcePageType, object paratmer = null, object ignore = null)
        {
            NavigationHistory.Push(new NavigationHistoryItem
            {
                PageType = SourcePageType,
                Paratmers = paratmer,
                Item = PageBase.NavMain.SelectedItem
            });
            BaseFrame?.Navigate(SourcePageType, paratmer);
            GC.Collect();
        }

        public static void NavigateRefresh()
        {
            var peek = NavigationHistory.Peek();
            BaseFrame?.Navigate(peek.PageType, peek.Paratmers);
            GC.Collect();
        }

        public static void CollectGarbage()
        {
            NavigatePage(typeof(BlankPage));
            BaseFrame.Content = null;
            PageExpandedPlayer?.Dispose();
            PageExpandedPlayer = null;
            PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
            KawazuConv = null;
            ListedSongs.Clear();
        }
        public static void UIElement_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            var element = sender as UIElement;
            element.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        }

        public static void NavigateBack()
        {
            if (NavigationHistory.Count > 1)
                NavigationHistory.Pop();
            try
            {
                var bak = NavigationHistory.Peek();
                Common.BaseFrame?.Navigate(bak.PageType, bak.Paratmers);
                NavigatingBack = true;
                Common.PageBase.NavMain.SelectedItem = bak.Item;
                NavigatingBack = false;
                GC.Collect();
            }
            catch
            {
            }
        }

        public static bool NavigatingBack = false;
    }

    internal class Setting : INotifyPropertyChanged
    {
        public int lyricSize
        {
            get
            {
                int ret = 0;
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

        public int romajiSize
        {
            get
            {
                return GetSettings<int>("romajiSize", 15);
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["romajiSize"] = value;
                OnPropertyChanged();
            }
        }

        public bool lyricAlignment
        {
            get { return GetSettings<bool>("lyricAlignment", false); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["lyricAlignment"] = value;
                OnPropertyChanged();
            }
        }

        public string audioRate
        {
            get { return GetSettings<string>("audioRate", "320000"); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["audioRate"] = value;
                OnPropertyChanged();
            }
        }

        public int Volume
        {
            get
            {
                try
                {
                    return GetSettings<int>("Volume", 50);
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
                    return GetSettings<string>("downloadDir", KnownFolders.MusicLibrary
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

        public bool toastLyric
        {
            get { return GetSettings<bool>("toastLyric", false); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["toastLyric"] = value;
                OnPropertyChanged();
            }
        }

        public bool expandAnimation
        {
            get { return GetSettings<bool>("expandAnimation", true); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["expandAnimation"] = value ? "true" : "false";
                OnPropertyChanged();
            }
        }

        public int songRollType
        {
            get { return GetSettings<int>("songRollType", 0); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["songRollType"] = value;
                OnPropertyChanged();
            }
        }

        public bool songUrlLazyGet
        {
            get { return GetSettings<bool>("songUrlLazyGet", true); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] = value;
                OnPropertyChanged();
            }
        }
        
        public int themeRequest
        {
            // 0 - 未设置   1 - 浅色  2 - 深色
            get { return GetSettings<int>("themeRequest", 0); }
            set
            {
                ApplicationData.Current.LocalSettings.Values["themeRequest"] = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public async void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => { PropertyChanged(this, new PropertyChangedEventArgs(propertyName)); });
        }

        public static T GetSettings<T>(string propertyName, T defaultValue)
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey(propertyName) &&
                    ApplicationData.Current.LocalSettings.Values[propertyName] != null &&
                    !string.IsNullOrEmpty(ApplicationData.Current.LocalSettings.Values[propertyName].ToString()))
                {
                    //超长的IF
                    return (T)ApplicationData.Current.LocalSettings.Values[propertyName];
                }
                else
                {
                    return defaultValue;
                }
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
                for (var i = 0; i < list.Count; i++)
                    if (list[i] == Text)
                        list.Remove(list[i]);
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

        public static void ClearHistory()
        {
            var list = new List<string>();
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["songHistory"] = JsonConvert.SerializeObject(list);
            ApplicationData.Current.LocalSettings.Values["searchHistory"] = JsonConvert.SerializeObject(list);
        }

        public static async Task<List<NCSong>> GetNCSongHistory()
        {
            var retsongs = new List<NCSong>();
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                new Dictionary<string, object>
                {
                    ["ids"] = string.Join(",",
                        JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                            .Values["songHistory"].ToString()))
                });
            if (isOk) return json["songs"].ToArray().Select(t => NCSong.CreateFromJson(t)).ToList();
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
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Batch, queries);
            var ret = new List<NCPlayList>();
            for (var k = 0; k < json.Count - 1; k++)
                ret.Add(NCPlayList.CreateFromJson(json["/api/v6/playlist/detail" + new string('/', k)]["playlist"]));
            return ret;
        }

        public static List<string> GetSearchHistory()
        {
            return JsonConvert.DeserializeObject<List<string>>(ApplicationData.Current.LocalSettings
                .Values["searchHistory"].ToString());
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

        public static byte[] ComputeMd5(this byte[] value)
        {
            var md5 = MD5.Create();
            return md5.ComputeHash(value);
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