using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml.Navigation;
using System.Diagnostics;
using QRCoder;
using Windows.Storage.Streams;
using Microsoft.AppCenter.Crashes;
using System.Net;
using HyPlayer.Controls;


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class BasePage : Page
    {
        private List<NavigationViewItem> selectionHistory;
        private bool IsNavBack;

        public BasePage()
        {
            InitializeComponent();
            Common.PageBase = this;
            selectionHistory = new List<NavigationViewItem>();

            if (Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Desktop")
            {

                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                Window.Current.SetTitleBar(AppTitleBar);

                LoadLoginData();
                Common.BaseFrame = BaseFrame;
                NavMain.SelectedItem = NavMain.MenuItems[0];
                //Common.BaseFrame.Navigate(typeof(Home));上一行代码会引发NavMain的SelectionChanged事件，不需要重复导航
            }
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string)
                LoginDone();
        }
        private void PhraseCookie(string cookielines)
        {
            try
            {
                foreach (string cookieHeader in cookielines.Split("\r\n"))
                {
                    if (string.IsNullOrEmpty(cookieHeader)) continue;
                    var cookie = new Cookie();
                    var CookieDic = new Dictionary<string, string>();
                    var arr1 = cookieHeader.Split(';').ToList();
                    var arr2 = arr1[0].Trim().Split('=');
                    cookie.Name = arr2[0];
                    cookie.Value = arr2[1];
                    arr1.RemoveAt(0);
                    if (string.IsNullOrEmpty(cookie.Value))
                        continue;
                    foreach (string cookiediac in arr1)
                    {
                        try
                        {
                            string[] cookiesetarr = cookiediac.Trim().Split('=');
                            switch (cookiesetarr[0].Trim().ToLower())
                            {
                                case "expires":
                                    cookie.Expires = DateTime.Parse(cookiesetarr[1].Trim());
                                    break;
                                case "max-age":
                                    cookie.Expires = DateTime.Now.AddSeconds(int.Parse(cookiesetarr[1]));
                                    break;
                                case "domain":
                                    cookie.Domain = cookiesetarr[1].Trim();
                                    break;
                                case "path":
                                    cookie.Path = cookiesetarr[1].Trim().Replace("%x2F", "/");
                                    break;
                                case "secure":
                                    cookie.Secure = cookiesetarr[1].Trim().ToLower() == "true";
                                    break;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    Common.ncapi.Cookies.Add(cookie);
                }
            }
            catch (Exception)
            {

            }
        }

        private async void LoadLoginData()
        {
            try
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("cookie") && string.IsNullOrEmpty(ApplicationData.Current.LocalSettings.Values["cookie"].ToString()))
                    return;
                PhraseCookie(ApplicationData.Current.LocalSettings.Values["cookie"].ToString());
                var (retOk, LoginStatus) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
                if (retOk)
                {
                    LoginDone();
                }
            }
            catch
            {
                // ignored
            }
        }

        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(TextBoxAccount.Text) || String.IsNullOrWhiteSpace(TextBoxPassword.Password))
            {
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Message = "用户名或密码不能为空";
                return;
            }

            ButtonLogin.IsEnabled = false;
            ButtonLogin.Content = "登录中......";
            bool isOk;
            JObject json;
            try
            {
                Dictionary<string, object> queries = new Dictionary<string, object>();
                string account = TextBoxAccount.Text;
                bool isPhone = Regex.Match(account, "^[0-9]+$").Success;
                queries[isPhone ? "phone" : "email"] = account;
                queries["password"] = TextBoxPassword.Password;
                (isOk, json) = await Common.ncapi.RequestAsync(
                    isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
                if (!isOk || json["code"].ToString() != "200")
                {
                    ButtonLogin.Visibility = Visibility.Visible;
                    InfoBarLoginHint.IsOpen = true;
                    InfoBarLoginHint.Title = "登录失败";
                    ButtonLogin.Content = "登录";
                    ButtonLogin.IsEnabled = true;
                    InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                    InfoBarLoginHint.Message = "登录失败 " + json["msg"];
                }
                else
                {
                    LoginDone();
                }
            }
            catch (Exception ex)
            {
                ButtonLogin.IsEnabled = true;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Severity = InfoBarSeverity.Error;
                InfoBarLoginHint.Message = "登录失败 " + ex.ToString();
                Crashes.TrackError(ex);
            }
        }

        private void ButtonCloseLoginForm_Click(object sender, RoutedEventArgs e)
        {
            DialogLogin.Hide();
            NavViewBack();
        }

        private void NavViewBack()
        {
            if (selectionHistory.Count == 0) return;
            selectionHistory.RemoveAt(selectionHistory.Count - 1);
            if (selectionHistory.Count != 0)
                NavMain.SelectedItem = selectionHistory.Last();
        }

        public async void LoginDone()
        {
            var (retOk, LoginStatus) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
            if (!LoginStatus["account"].HasValues) return;
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Title = "登录成功";
            //存储Cookie
            string cookiestr = "";
            foreach (Cookie cookie in Common.ncapi.Cookies)
            {
                string thiscookiestr = cookie.Name + "=" + cookie.Value;
                if (!string.IsNullOrEmpty(cookie.Domain))
                    thiscookiestr += "; Domain=" + cookie.Domain;
                if (cookie.Expires != DateTime.MinValue)
                    thiscookiestr += "; Expires=" + cookie.Expires.ToString("R");
                if (!string.IsNullOrEmpty(cookie.Path))
                    thiscookiestr += "; Path=" + cookie.Path;
                if (!cookie.Secure)
                    thiscookiestr += "; Secure";
                if (cookie.HttpOnly)
                    thiscookiestr += "; HttpOnly";
                cookiestr += thiscookiestr + "\r\n";
            }
            ApplicationData.Current.LocalSettings.Values["cookie"] = cookiestr;
            Common.LoginedUser = NCUser.CreateFromJson(LoginStatus["profile"]);
            Common.Logined = true;
            NavItemLogin.Content = Common.LoginedUser.name;
            NavItemLogin.Icon = new BitmapIcon() { UriSource = new Uri(Common.LoginedUser.avatar + "?param=" + StaticSource.PICSIZE_NAVITEM_USERAVATAR), ShowAsMonochrome = false };
            InfoBarLoginHint.Severity = InfoBarSeverity.Success;
            InfoBarLoginHint.Message = "欢迎 " + Common.LoginedUser.name;
            DialogLogin.Hide();
            NavViewBack();
            //加载我喜欢的歌
            _ = Task.Run((() =>
            {
                Common.Invoke((async () =>
               {
                   (bool isok, JObject js) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Likelist,
                       new Dictionary<string, object>() { { "uid", Common.LoginedUser.id } });
                   Common.LikedSongs = js["ids"].ToObject<List<string>>();
               }));
            }));

            _ = Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    //加载用户歌单
                    Microsoft.UI.Xaml.Controls.NavigationViewItem nowitem = NavItemsMyList;
                    (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                        new Dictionary<string, object>() { { "uid", Common.LoginedUser.id } });
                    if (isOk)
                    {
                        NavItemsLikeList.Visibility = Visibility.Visible;
                        NavItemsAddPlaylist.Visibility = Visibility.Visible;
                        NavItemsMyList.Visibility = Visibility.Visible;
                        NavItemsMyLovedPlaylist.Visibility = Visibility.Visible;
                        Common.MySongLists.Clear();
                        bool isliked = false;
                        foreach (JToken jToken in json["playlist"])
                        {
                            if (jToken["subscribed"].ToString() == "True")
                            {
                                NavItemsLikeList.MenuItems.Add(new NavigationViewItem()
                                {
                                    Content = jToken["name"].ToString(),
                                    Tag = "Playlist" + jToken["id"]
                                });
                            }
                            else
                            {
                                Common.MySongLists.Add(NCPlayList.CreateFromJson(jToken));
                                if (!isliked)
                                {
                                    isliked = true;
                                    continue;
                                }                                
                                NavItemsMyList.MenuItems.Add(new NavigationViewItem()
                                {
                                    Content = jToken["name"].ToString(),
                                    Tag = "Playlist" + jToken["id"]
                                });
                            }
                        }
                    }
                }));
            }));

            // 执行签到操作
            _ = Task.Run((() =>
            {
                Common.Invoke((() =>
                {
                    Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin);
                    Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin, new Dictionary<string, object>() { { "type", 1 } });
                    //刷播放量不?
                }));
            }));

            HyPlayList.OnMediaEnd += (hpi =>
            {
                // 播放数据
                _ = Task.Run((() =>
                {
                    Common.Invoke((() =>
                    {
                        if (!hpi.isOnline) return;
                        Common.ncapi.RequestAsync(CloudMusicApiProviders.Scrobble, new Dictionary<string, object>()
                        {
                            {"id",hpi.NcPlayItem.sid},
                            {"sourceId","-1"},
                            {"time","60" }
                        });
                    }));
                }));
            });

            HyPlayList.LoginDownCall();

        }


        private async void NavMain_OnSelectionChanged(NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            var nowitem = sender.SelectedItem as NavigationViewItem;
            if (!IsNavBack)
                selectionHistory.Add(nowitem);
            if (selectionHistory.Count > 1)
                NavMain.IsBackEnabled = true;
            IsNavBack = false;
            if (nowitem.Tag is null) return;
            if (nowitem.Tag.ToString() == "PersonalFM")
            {
                PersonalFM.InitPersonalFM();
                return;
            }

            if (nowitem.Tag.ToString() == "PageMe" && !Common.Logined)
            {
                foreach (Cookie ncapiCookie in Common.ncapi.Cookies)
                {
                    ncapiCookie.Expired = true;//清一遍Cookie防止出错
                }
                InfoBarLoginHint.IsOpen = true;
                await DialogLogin.ShowAsync();
                return;
            }
            if (nowitem.Tag.ToString() == "SonglistCreate")
            {
                await new CreateSonglistDialog().ShowAsync();
                _ = Task.Run((() =>
                {
                    Common.Invoke((async () =>
                    {
                        //加载用户歌单
                        Microsoft.UI.Xaml.Controls.NavigationViewItem item = NavItemsMyList;
                        (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                            new Dictionary<string, object>() { { "uid", Common.LoginedUser.id } });
                        if (isOk)
                        {
                            NavItemsLikeList.Visibility = Visibility.Visible;
                            NavItemsMyList.Visibility = Visibility.Visible;
                            Common.MySongLists.Clear();
                            NavItemsMyList.MenuItems.Clear();
                            foreach (JToken jToken in json["playlist"])
                            {
                                if (jToken["subscribed"].ToString() == "True")
                                {
                                    NavItemsLikeList.MenuItems.Add(new NavigationViewItem()
                                    {
                                        Content = jToken["name"].ToString(),
                                        Tag = "Playlist" + jToken["id"]
                                    });
                                }
                                else
                                {
                                    Common.MySongLists.Add(NCPlayList.CreateFromJson(jToken));
                                    NavItemsMyList.MenuItems.Add(new NavigationViewItem()
                                    {
                                        Content = jToken["name"].ToString(),
                                        Tag = "Playlist" + jToken["id"]
                                    });
                                }
                            }
                        }
                    }));
                }));
                return;
            }
            if (nowitem.Tag.ToString() == "SonglistMyLike")
            {
                Common.BaseFrame.Navigate(typeof(Pages.SongListDetail), Common.MySongLists[0].plid,
                    new EntranceNavigationTransitionInfo());
                return;
            }

            if (nowitem.Tag.ToString().StartsWith("Playlist"))
            {
                Common.BaseFrame.Navigate(typeof(Pages.SongListDetail), nowitem.Tag.ToString().Substring(8),
                    new EntranceNavigationTransitionInfo());
            }

            switch (nowitem.Tag.ToString())
            {
                case "PageMe":
                    Common.BaseFrame.Navigate(typeof(Pages.Me), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageSearch":
                    Common.BaseFrame.Navigate(typeof(Pages.Search), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageHome":
                    Common.BaseFrame.Navigate(typeof(Pages.Home), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageSettings":
                    Common.BaseFrame.Navigate(typeof(Pages.Settings), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageLocal":
                    Common.BaseFrame.Navigate(typeof(Pages.LocalMusicPage), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageHistory":
                    Common.BaseFrame.Navigate(typeof(Pages.History), null, new EntranceNavigationTransitionInfo());
                    break;
            }
        }

        private void OnNavigateBack(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            try
            {
                if (Common.BaseFrame.CanGoBack)
                {
                    Common.BaseFrame.GoBack();
                }

                NavViewBack();

                if (selectionHistory.Count <= 1)
                    NavMain.IsBackEnabled = false;
            }
            catch (Exception)
            {
                //ignore
            }
        }

        private void TextBoxAccount_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                TextBoxPassword.Focus(FocusState.Keyboard);
            }
        }

        private void TextBoxPassword_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ButtonLogin_OnClick(null, null);
            }
        }

        private async void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as Pivot).SelectedIndex == 1)
            {
                (bool isOk, JObject key) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey, new Dictionary<string, object>() { { "timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000 } });
                if (isOk)
                    ReFreshQr(key);


                while (true)
                {

                    (bool a, JObject res) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrCheck, new Dictionary<string, object>() { { "key", key["unikey"].ToString() } });
                    if (res["code"].ToString() == "800")
                    {
                        (isOk, key) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey, new Dictionary<string, object>() { { "timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000 } });
                        if (isOk)
                            ReFreshQr(key);
                    }
                    else if (res["code"].ToString() == "801")
                    {
                        InfoBarLoginHint.Title = "请扫描上方二维码登录";
                        //
                    }
                    else if (res["code"].ToString() == "803")
                    {
                        InfoBarLoginHint.IsOpen = true;
                        InfoBarLoginHint.Title = "登录成功";
                        ButtonLogin.Content = "登录成功";
                        LoginDone();
                        break;
                    }
                    else if (res["code"].ToString() == "802")
                    {
                        InfoBarLoginHint.Title = "请在手机上授权登录";
                    }

                    await Task.Delay(2000);
                }
            }
            else
            {
                InfoBarLoginHint.Title = "登录代表你同意相关条款";
            }
        }
        private async void ReFreshQr(JObject key)
        {


            Uri QrUri = new Uri("https://music.163.com/login?codekey=" + key["unikey"].ToString());
            var img = new BitmapImage();

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(QrUri.ToString(), QRCodeGenerator.ECCLevel.M);
            var qrCode = new BitmapByteQRCode(qrData);
            var qrImage = qrCode.GetGraphic(20);
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(qrImage);
                    await writer.StoreAsync();
                }
                await img.SetSourceAsync(stream);
                QrContainer.Source = img;
            }
            InfoBarLoginHint.Title = "请扫描上方二维码登录";
        }

        private void ThirdPartyLogin_Click(object sender, RoutedEventArgs e)
        {
            DialogLogin.Hide();
            BaseFrame.Navigate(typeof(ThirdPartyLogin), (sender as Button).Tag.ToString());

        }

        private void NavigationViewItem_Tapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            NavMain.SelectedItem = sender;
        }


        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            Common.BaseFrame.Navigate(typeof(Pages.Search), sender.Text, new EntranceNavigationTransitionInfo());
        }

        private async void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(sender.Text))
            {
                AutoSuggestBox_GotFocus(sender, null);
                return;
            }

            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchSuggest,
                new Dictionary<string, object>() { { "keywords", sender.Text }, { "type", "mobile" } });

            if (isOk && json["result"]["allMatch"] != null && json["result"]["allMatch"].HasValues)
            {
                sender.ItemsSource = json["result"]["allMatch"].ToArray().ToList().Select(t => t["keyword"].ToString())
                    .ToList();
            }
        }

        private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender,
            AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            sender.Text = args.SelectedItem.ToString();
        }

        private void AutoSuggestBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ((AutoSuggestBox)sender).ItemsSource = null;
        }

        private async void AutoSuggestBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace((sender as AutoSuggestBox)?.Text))
            {
                (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchHot);
                if (isOk)
                {
                    ((AutoSuggestBox)sender).ItemsSource =
                        json["result"]["hots"].ToArray().ToList().Select(t => t["first"].ToString());
                }
            }
        }
    }
}
