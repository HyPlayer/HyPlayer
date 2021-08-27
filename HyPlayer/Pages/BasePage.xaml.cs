using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Profile;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using Microsoft.AppCenter.Crashes;
using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using QRCoder;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Controls.Primitives;


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class BasePage : Page
    {
        private string nowqrkey;
        private string nowplid;
        public BasePage()
        {
            InitializeComponent();
            Common.PageBase = this;
            Common.GlobalTip = TheTeachingTip;
            if (HyPlayList.Player == null)
                HyPlayList.InitializeHyPlaylist();
            if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Desktop")
            {
                CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
                var titleBar = ApplicationView.GetForCurrentView().TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                Window.Current.SetTitleBar(AppTitleBar);

            }
            else if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            {
                bool result = ApplicationViewScaling.TrySetDisableLayoutScaling(true);
                ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
            }
            ApplicationView.TerminateAppOnFinalViewClose = false;
            Common.BaseFrame = BaseFrame;
            BaseFrame.IsNavigationStackEnabled = false;
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
        }

        private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs args)
        {
            if (args.CurrentPoint.Properties.IsXButton1Pressed)
                if (Common.isExpanded)
                    Common.BarPlayBar.ButtonCollapse_OnClick(null, null);
                else
                    Common.NavigateBack();
        }

        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            if (args.VirtualKey == VirtualKey.GamepadB)
            {
                if (Common.isExpanded)
                    Common.BarPlayBar.ButtonCollapse_OnClick(null, null);
                else
                    Common.NavigateBack();
                args.Handled = true;
            }

            if (args.VirtualKey == VirtualKey.GamepadY)
                if (HyPlayList.isPlaying)
                    HyPlayList.Player.Pause();
                else if (!HyPlayList.isPlaying) HyPlayList.Player.Play();

            if (args.VirtualKey == VirtualKey.Escape)
                if (Common.isExpanded)
                    Common.BarPlayBar.ButtonCollapse_OnClick(null, null);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadLoginData();
            /*
            if (e.Parameter is string)
                LoginDone();
            */
        }

        private void PhraseCookie(string cookielines)
        {
            try
            {
                foreach (var cookieHeader in cookielines.Split("\r\n"))
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
                    foreach (var cookiediac in arr1)
                        try
                        {
                            var cookiesetarr = cookiediac.Trim().Split('=');
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
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey("cookie") &&
                    !string.IsNullOrEmpty(ApplicationData.Current.LocalSettings.Values["cookie"].ToString()))
                {
                    PhraseCookie(ApplicationData.Current.LocalSettings.Values["cookie"].ToString());
                    var (retOk, LoginStatus) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
                    if (retOk) await LoginDone();
                }
                else
                {
                    Common.NavigatePage(typeof(Welcome));
                }
            }
            catch
            {
                // ignored
            }
        }

        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TextBoxAccount.Text) || string.IsNullOrWhiteSpace(TextBoxPassword.Password))
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
                var queries = new Dictionary<string, object>();
                var account = TextBoxAccount.Text;
                var isPhone = Regex.Match(account, "^[0-9]+$").Success;
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
                    await LoginDone();
                    Common.NavigatePage(typeof(Home));
                }
            }
            catch (Exception ex)
            {
                ButtonLogin.IsEnabled = true;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Severity = InfoBarSeverity.Error;
                InfoBarLoginHint.Message = "登录失败 " + ex;
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
            Common.NavigateBack();
        }

        public async Task<bool> LoginDone()
        {
            var (retOk, LoginStatus) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
            if (!LoginStatus["account"].HasValues) return false;
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Title = "登录成功";
            //存储Cookie
            var cookiestr = "";
            foreach (Cookie cookie in Common.ncapi.Cookies)
            {
                var thiscookiestr = cookie.Name + "=" + cookie.Value;
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
            NavItemLogin.Icon = new BitmapIcon
            {
                UriSource = new Uri(Common.LoginedUser.avatar + "?param=" + StaticSource.PICSIZE_NAVITEM_USERAVATAR),
                ShowAsMonochrome = false
            };
            InfoBarLoginHint.Severity = InfoBarSeverity.Success;
            InfoBarLoginHint.Message = "欢迎 " + Common.LoginedUser.name;
            DialogLogin.Hide();
            //加载我喜欢的歌
            _ = Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    var (isok, js) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Likelist,
                        new Dictionary<string, object> { { "uid", Common.LoginedUser.id } });
                    Common.LikedSongs = js["ids"].ToObject<List<string>>();
                });
            });

            LoadSongList();

            // 执行签到操作
            _ = Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin);
                    Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin,
                        new Dictionary<string, object> { { "type", 1 } });
                    //刷播放量不?
                });
            });

            HyPlayList.LoginDownCall();
            ((App)App.Current).InitializeJumpList();
            NavMain.SelectedItem = NavItemLogin;
            Common.NavigatePage(typeof(Me));
            return true;
        }

        public async void LoadSongList()
        {
            //加载用户歌单
            var nowitem = NavItemsMyList;
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                new Dictionary<string, object> { { "uid", Common.LoginedUser.id } });
            if (isOk)
            {
                NavItemsLikeList.MenuItems.Clear();
                NavItemsMyList.MenuItems.Clear();
                NavItemsLikeList.Visibility = Visibility.Visible;
                NavItemsAddPlaylist.Visibility = Visibility.Visible;
                NavItemsMyList.Visibility = Visibility.Visible;
                NavItemsMyLovedPlaylist.Visibility = Visibility.Visible;
                Common.MySongLists.Clear();
                var isliked = false;
                foreach (var jToken in json["playlist"])
                    if (jToken["subscribed"].ToString() == "True")
                    {
                        var item = new NavigationViewItem
                        {
                            Content = jToken["name"].ToString(),
                            Tag = "Playlist" + jToken["id"],
                            IsRightTapEnabled = true,
                            Icon = new FontIcon()
                            {
                                FontFamily = Application.Current.Resources["SymbolThemeFontFamily"] as FontFamily,
                                Glyph = "\uE142"
                            }
                        };
                        item.RightTapped += (_, __) =>
                        {
                            nowplid = jToken["id"].ToString();
                            ItemPublicPlayList.Visibility = Visibility.Collapsed;
                            PlaylistFlyout.ShowAt((FrameworkElement)_);
                        };
                        NavItemsLikeList.MenuItems.Add(item);
                    }
                    else
                    {
                        if (!isliked)
                        {
                            isliked = true;
                            continue;
                        }
                        Common.MySongLists.Add(NCPlayList.CreateFromJson(jToken));
                        var item = new NavigationViewItem
                        {
                            Icon = new FontIcon()
                            {
                                FontFamily = Application.Current.Resources["SymbolThemeFontFamily"] as FontFamily,
                                Glyph = jToken["privacy"].ToString() == "0" ? "\uE142" : "\uE72E",
                                Foreground = jToken["privacy"].ToString() == "0" ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(255, 255, 214, 133))
                            },
                            Content = jToken["name"].ToString(),
                            Tag = "Playlist" + jToken["id"],
                            IsRightTapEnabled = true
                        };
                        item.RightTapped += (_, __) =>
                        {
                            nowplid = jToken["id"].ToString();
                            ItemPublicPlayList.Visibility = jToken["privacy"].ToString() == "0" ? Visibility.Collapsed : Visibility.Visible;
                            PlaylistFlyout.ShowAt((FrameworkElement)_);
                        };
                        NavItemsMyList.MenuItems.Add(item);
                    }
            }
        }

        private async void NavMain_OnSelectionChanged(NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            if (Common.NavigatingBack) return;
            var nowitem = sender.SelectedItem as NavigationViewItem;
            if (Common.NavigationHistory.Count > 1)
                NavMain.IsBackEnabled = true;
            if (nowitem.Tag is null) return;

            if (nowitem.Tag.ToString() == "PageMe" && !Common.Logined)
            {
                foreach (Cookie ncapiCookie in Common.ncapi.Cookies) ncapiCookie.Expired = true; //清一遍Cookie防止出错
                InfoBarLoginHint.IsOpen = true;
                await DialogLogin.ShowAsync();
                return;
            }

            if (nowitem.Tag.ToString() == "MusicCloud")
            {
                Common.NavigatePage(typeof(MusicCloudPage));
            }

            if (nowitem.Tag.ToString() == "DailyRcmd")
            {
                Common.NavigatePage(typeof(SongListDetail), new NCPlayList()
                {
                    cover = "ms-appx:/Assets/icon.png",
                    creater = new NCUser()
                    {
                        avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
                        id = "1",
                        name = "网易云音乐",
                        signature = "网易云音乐官方账号 "
                    },
                    plid = "-666",
                    subscribed = false,
                    name = "每日歌曲推荐",
                    desc = "根据你的口味生成，每天6:00更新"
                });
            }

            if (nowitem.Tag.ToString() == "SonglistMyLike")
            {
                Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid,
                    new EntranceNavigationTransitionInfo());
                return;
            }

            if (nowitem.Tag.ToString().StartsWith("Playlist"))
                Common.NavigatePage(typeof(SongListDetail), nowitem.Tag.ToString().Substring(8),
                    new EntranceNavigationTransitionInfo());

            switch (nowitem.Tag.ToString())
            {
                case "PageMe":
                    Common.NavigatePage(typeof(Me), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageSearch":
                    Common.NavigatePage(typeof(Search), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageHome":
                    Common.NavigatePage(typeof(Home), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageSettings":
                    Common.NavigatePage(typeof(Settings), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageLocal":
                    Common.NavigatePage(typeof(LocalMusicPage), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageHistory":
                    Common.NavigatePage(typeof(History), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageFavorite":
                    Common.NavigatePage(typeof(PageFavorite), null, new EntranceNavigationTransitionInfo());
                    break;
            }
        }

        // Invoked events of not-for-navigation items can be handled separately.
        // Meanwhile we set "SelectsOnInvoked" property of these items "False" to avoid the navigation pane indicator being set to them.
        private async void NavMain_ItemInvoked(NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
        {
            string invokedItemTag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
            if (invokedItemTag is null || invokedItemTag == string.Empty) return;
            switch (invokedItemTag)
            {
                case "SonglistCreate":
                    {
                        await new CreateSonglistDialog().ShowAsync();
                        LoadSongList();
                        break;
                    }
                case "PersonalFM":
                    {
                        PersonalFM.InitPersonalFM();
                        break;
                    }
            }
        }

        private void OnNavigateBack(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            try
            {
                NavViewBack();
            }
            catch (Exception)
            {
                //ignore
            }
        }

        private void TextBoxAccount_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) TextBoxPassword.Focus(FocusState.Keyboard);
        }

        private void TextBoxPassword_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) ButtonLogin_OnClick(null, null);
        }

        private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as Pivot).SelectedIndex == 1)
            {
                LoadQr(null, null);
            }
            else
            {
                InfoBarLoginHint.Title = "登录代表你同意相关条款";
            }
        }

        private async void LoadQr(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            var (isOk, key) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey,
                    new Dictionary<string, object> { { "timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000 } });
            if (isOk)
                ReFreshQr(key);
            else
            {
                InfoBarLoginHint.Title = "请点击二维码刷新";
                return;
            }

            nowqrkey = key["unikey"].ToString();
            while (!Common.Logined && nowqrkey == key["unikey"].ToString())
            {
                var (a, res) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrCheck,
                    new Dictionary<string, object> { { "key", key["unikey"].ToString() } });
                if (res["code"].ToString() == "800")
                {
                    (isOk, key) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey,
                        new Dictionary<string, object>
                            {{"timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000}});
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
                    await LoginDone();
                    break;
                }
                else if (res["code"].ToString() == "802")
                {
                    InfoBarLoginHint.Title = "请在手机上授权登录";
                }

                await Task.Delay(2000);
            }
        }

        private async void ReFreshQr(JObject key)
        {
            var QrUri = new Uri("https://music.163.com/login?codekey=" + key["unikey"]);
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
            Common.NavigatePage(typeof(Search), sender.Text, new EntranceNavigationTransitionInfo());
        }

        private async void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(sender.Text))
            {
                AutoSuggestBox_GotFocus(sender, null);
                return;
            }

            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchSuggest,
                new Dictionary<string, object> { { "keywords", sender.Text }, { "type", "mobile" } });

            if (isOk && json["result"] != null && json["result"]["allMatch"] != null &&
                json["result"]["allMatch"].HasValues)
                sender.ItemsSource = json["result"]["allMatch"].ToArray().ToList().Select(t => t["keyword"].ToString())
                    .ToList();
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
            if (string.IsNullOrWhiteSpace((sender as AutoSuggestBox)?.Text))
            {
                var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchHot);
                if (isOk)
                    ((AutoSuggestBox)sender).ItemsSource =
                        json["result"]["hots"].ToArray().ToList().Select(t => t["first"].ToString());
            }
        }

        private void BtnScaleQrCode_Click(object sender, RoutedEventArgs e)
        {
            DialogLogin.Width = 550;
            DialogLogin.Height = Window.Current.Bounds.Height;
            LoginPivot.Width = 520;
            LoginPivot.Height = 550;
            QrContainer.Height = 500;
            QrContainer.Width = QrContainer.Height;
        }

        private void NavMain_DisplayModeChanged(NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewDisplayModeChangedEventArgs args)
        {
            const int topIndent = 16;
            const int expandedIndent = 48;
            int minimalIndent = 104;
            if (NavMain.IsBackButtonVisible.Equals(Microsoft.UI.Xaml.Controls.NavigationViewBackButtonVisible.Collapsed))
            {
                minimalIndent = 48;
            }

            Thickness currMargin = AppTitleBar.Margin;
            if (sender.PaneDisplayMode == Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Top)
            {
                AppTitleBar.Margin = new Thickness(topIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
            }
            else if (sender.DisplayMode == Microsoft.UI.Xaml.Controls.NavigationViewDisplayMode.Minimal)
            {
                AppTitleBar.Margin = new Thickness(minimalIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
            }
            else
            {
                AppTitleBar.Margin = new Thickness(expandedIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
            }
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistPrivacy, new Dictionary<string, object>()
            {
                { "id", nowplid }
            });
            Common.ShowTeachingTip(isOk ? "成功公开歌单" : "公开歌单失败");
            LoadSongList();
        }

        private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
        {
            var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDelete, new Dictionary<string, object>()
            {
                {"ids",nowplid }
            });
            Common.ShowTeachingTip(isOk ? "成功删除" : "删除失败");
            LoadSongList();
        }
    }
}
