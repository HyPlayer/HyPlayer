#region

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
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
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
using NavigationViewBackButtonVisible = Microsoft.UI.Xaml.Controls.NavigationViewBackButtonVisible;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewDisplayMode;
using NavigationViewDisplayModeChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewDisplayModeChangedEventArgs;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;
using NavigationViewPaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

#endregion


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class BasePage : Page
{
    private string nowplid;
    private string nowqrkey;

    public BasePage()
    {
        InitializeComponent();
        Common.PageBase = this;
        Common.GlobalTip = TheTeachingTip;
        HyPlayList.OnTimerTicked += () => Common.RollTeachingTip();
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
            var result = ApplicationViewScaling.TrySetDisableLayoutScaling(true);
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
        }

        ApplicationView.TerminateAppOnFinalViewClose = false;
        Common.BaseFrame = BaseFrame;
        BaseFrame.IsNavigationStackEnabled = !Common.Setting.forceMemoryGarbage;
        Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
    }

    private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs args)
    {
        if (args.CurrentPoint.Properties.IsXButton1Pressed)
            if (Common.isExpanded)
                Common.BarPlayBar.CollapseExpandedPlayer();
            else
                Common.NavigateBack();
    }

    private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
    {
        if (args.VirtualKey == VirtualKey.GamepadB)
        {
            if (Common.isExpanded)
                Common.BarPlayBar.CollapseExpandedPlayer();
            else
                Common.NavigateBack();
            args.Handled = true;
        }

        if (args.VirtualKey == VirtualKey.GamepadY)
            if (HyPlayList.IsPlaying)
                HyPlayList.Player.Pause();
            else if (!HyPlayList.IsPlaying) HyPlayList.Player.Play();

        if (args.VirtualKey == VirtualKey.Escape)
            if (Common.isExpanded)
                Common.BarPlayBar.CollapseExpandedPlayer();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var dialog = new ContentDialog();
        if (!Common.Setting.DisablePopUp)
        {
            dialog.Title = "重要提示";
            dialog.Content = "本软件仅供学习交流使用，下载后请在 24 小时内删除。\r\n请勿使用此软件登录网易云音乐或进行违反网易云音乐用户协议的行为";
            dialog.CloseButtonText = "我已知晓";
            dialog.PrimaryButtonText = "退出软件";
            dialog.IsPrimaryButtonEnabled = true;
            dialog.PrimaryButtonClick += (_, _) => Application.Current.Exit();
            await dialog.ShowAsync();
        }
        // 不要阻塞页面加载
        _ = UpdateManager.PopupVersionCheck(true);
        // Fire and Forget
        _ = LoadLoginData();
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

    private async Task LoadLoginData()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey("cookie") &&
                !string.IsNullOrEmpty(ApplicationData.Current.LocalSettings.Values["cookie"].ToString()))
            {
                PhraseCookie(ApplicationData.Current.LocalSettings.Values["cookie"].ToString());
                try
                {
                    await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
                    await LoginDone();
                }
                catch
                {
                    // ignored
                }
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
        JObject json;
        try
        {
            var queries = new Dictionary<string, object>();
            var account = TextBoxAccount.Text;
            var isPhone = Regex.Match(account, "^[0-9]+$").Success;
            if (account.StartsWith('+'))
            {
                isPhone = true;
                // get the string between '+' and ' '
                queries["countrycode"] = account.Substring(1, account.IndexOf(' ') - 1);
                account = account.Substring(account.IndexOf(' ') + 1);
            }

            queries[isPhone ? "phone" : "email"] = account;
            queries["password"] = TextBoxPassword.Password;
            json = await Common.ncapi.RequestAsync(
                isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
            if (json?["code"]?.ToString() != "200")
            {
                ButtonLogin.Visibility = Visibility.Visible;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Title = "登录失败";
                ButtonLogin.Content = "登录";
                ButtonLogin.IsEnabled = true;
                InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                InfoBarLoginHint.Message = "登录失败 " + json["msg"] + json["message"];
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
        JObject LoginStatus;
        try
        {
            LoginStatus = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginStatus);
        }
        catch (Exception e)
        {
            Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            return false;
        }

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
        if (LoginStatus?["profile"].HasValues ?? false)
            Common.LoginedUser = NCUser.CreateFromJson(LoginStatus["profile"]);
        else
            Common.LoginedUser = new NCUser
            {
                avatar = "ms-appx:///Assets/icon.png",
                id = LoginStatus["account"]["id"].ToString(),
                name = LoginStatus["account"]["userName"].ToString(),
                signature = "此账号未进行手机号验证, 请使用网易云音乐客户端登录后再继续操作"
            };

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
        _ = LoadMyLikelist();
        _ = LoadSongList();

        // 执行签到操作
        DoDailySign();

        // 播放信息记录
        HyPlayList.OnMediaEnd += Scrobble;

        HyPlayList.LoginDoneCall();
        _ = ((App)Application.Current).InitializeJumpList();
        NavMain.SelectedItem = NavItemLogin;
        //Common.NavigatePage(typeof(Me));
        return true;
    }

    public async void Scrobble(HyPlayItem item)
    {
        // 播放数据记录
        if (item.ItemType != HyPlayItemType.Netease && Common.Setting.doScrobble /* || Common.IsInFm ||
            string.IsNullOrEmpty(HyPlayList.PlaySourceId)*/) return;
        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Scrobble, new Dictionary<string, object>
        {
            { "id", item.PlayItem.Id },
            { "sourceId", HyPlayList.PlaySourceId ?? "-1" },
            { "time", TimeSpan.FromMilliseconds(item.PlayItem.LengthInMilliseconds).TotalSeconds }
        });
    }

    private static void DoDailySign()
    {
        _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin);
        _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.DailySignin,
            new Dictionary<string, object> { { "type", 1 } });
    }

    private static async Task LoadMyLikelist()
    {
        try
        {
            var js = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Likelist,
                new Dictionary<string, object> { { "uid", Common.LoginedUser.id } });
            Common.LikedSongs = js["ids"].ToObject<List<string>>();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    public async Task LoadSongList()
    {
        //加载用户歌单
        var nowitem = NavItemsMyList;
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                new Dictionary<string, object> { { "uid", Common.LoginedUser.id } });

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
                        Icon = new FontIcon
                        {
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
                    Common.MySongLists.Add(NCPlayList.CreateFromJson(jToken));
                    if (!isliked)
                    {
                        isliked = true;
                        continue;
                    }

                    var item = new NavigationViewItem
                    {
                        Icon = new FontIcon
                        {
                            Glyph = jToken["privacy"].ToString() == "0" ? "\uE142" : "\uE72E"
                        },
                        Content = jToken["name"].ToString(),
                        Tag = "Playlist" + jToken["id"],
                        IsRightTapEnabled = true
                    };
                    if (jToken["privacy"].ToString() != "0")
                        item.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 211, 39, 100));

                    item.RightTapped += (_, __) =>
                    {
                        nowplid = jToken["id"].ToString();
                        ItemPublicPlayList.Visibility = jToken["privacy"].ToString() == "0"
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                        PlaylistFlyout.ShowAt((FrameworkElement)_);
                    };
                    NavItemsMyList.MenuItems.Add(item);
                }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
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
            await DialogLogin.ShowAsync();         
            return;
        }

        if (nowitem.Tag.ToString() == "MusicCloud") Common.NavigatePage(typeof(MusicCloudPage));

        if (nowitem.Tag.ToString() == "DailyRcmd")
            Common.NavigatePage(typeof(SongListDetail), new NCPlayList
            {
                cover = "ms-appx:/Assets/icon.png",
                creater = new NCUser
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
    private async void NavMain_ItemInvoked(NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        var invokedItemTag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (invokedItemTag is null || invokedItemTag == string.Empty) return;
        switch (invokedItemTag)
        {
            case "SonglistCreate":
            {
                await new CreateSonglistDialog().ShowAsync();
                _ = LoadSongList();
                break;
            }
            case "PersonalFM":
            {
                PersonalFM.InitPersonalFM();
                break;
            }
            case "HeartBeat":
                _ = LoadHeartBeat();
                break;
        }
    }

    private async Task LoadHeartBeat()
    {
        HyPlayList.RemoveAllSong();
        try
        {
            var jsoon = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                new Dictionary<string, object> { { "id", Common.MySongLists[0].plid } });
            var jsona = await Common.ncapi.RequestAsync(
                CloudMusicApiProviders.PlaymodeIntelligenceList,
                new Dictionary<string, object>
                {
                    { "pid", Common.MySongLists[0].plid },
                    { "id", jsoon["playlist"]["trackIds"][0]["id"].ToString() }
                });

            var Songs = new List<NCSong>();
            foreach (var token in jsona["data"])
            {
                var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                Songs.Add(ncSong);
            }

            try
            {
                HyPlayList.AppendNcSongs(Songs);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(0);
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
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
            LoadQr(null, null);
        else
            InfoBarLoginHint.Title = "登录代表你同意相关条款";
    }

    private async void LoadQr(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
    {
        try
        {
            var key = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey,
                new Dictionary<string, object>
                    { { "timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000 } });

            _ = ReFreshQr(key);
            nowqrkey = key["unikey"].ToString();
            while (!Common.Logined && nowqrkey == key["unikey"].ToString())
            {
                var res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrCheck,
                    new Dictionary<string, object> { { "key", key["unikey"].ToString() } });
                if (res["code"].ToString() == "800")
                {
                    key = await Common.ncapi.RequestAsync(CloudMusicApiProviders.LoginQrKey,
                        new Dictionary<string, object>
                            { { "timestamp", (DateTime.Now.Ticks - 621356256000000000) / 10000 } });
                    try
                    {
                        _ = ReFreshQr(key);
                    }
                    catch (Exception ex)
                    {
                        Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
                    }
                }
                else if (res["code"].ToString() == "801")
                {
                    InfoBarLoginHint.Title = "请扫描上方二维码登录";                 
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
        catch
        {
            InfoBarLoginHint.Title = "请点击二维码刷新";
        }
    }

    private async Task ReFreshQr(JObject key)
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


    private void BtnScaleQrCode_Click(object sender, RoutedEventArgs e)
    {
        DialogLogin.Width = 550;
        DialogLogin.Height = Window.Current.Bounds.Height;
        QrContainer.Height = 500;
        QrContainer.Width = QrContainer.Height;
    }

    private void NavMain_DisplayModeChanged(NavigationView sender,
        NavigationViewDisplayModeChangedEventArgs args)
    {
        const int topIndent = 16;
        const int expandedIndent = 48;
        var minimalIndent = 104;
        if (NavMain.IsBackButtonVisible.Equals(NavigationViewBackButtonVisible
                .Collapsed))
            minimalIndent = 48;

        var currMargin = AppTitleBar.Margin;
        if (sender.PaneDisplayMode == NavigationViewPaneDisplayMode.Top)
            AppTitleBar.Margin = new Thickness(topIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
        else if (sender.DisplayMode == NavigationViewDisplayMode.Minimal)
            AppTitleBar.Margin = new Thickness(minimalIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
        else
            AppTitleBar.Margin = new Thickness(expandedIndent, currMargin.Top, currMargin.Right, currMargin.Bottom);
    }

    private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistPrivacy,
                new Dictionary<string, object>
                {
                    { "id", nowplid }
                });
            Common.AddToTeachingTipLists("成功公开歌单");
            _ = LoadSongList();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("公开歌单失败", ex.Message);
        }
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDelete,
                new Dictionary<string, object>
                {
                    { "ids", nowplid }
                });
            Common.AddToTeachingTipLists("成功删除");
            _ = LoadSongList();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("删除失败", ex.Message);
        }
    }


    private void TheTeachingTip_OnCloseButtonClick(TeachingTip sender, object args)
    {
        Common.TeachingTipList.Clear();
    }

    private void SearchBtn_Clicked(object sender, RoutedEventArgs e)
    {

    }

    private void BtnSet_Click(object sender, RoutedEventArgs e)
    {

    }

    private void BtnLogOut_Click(object sender, RoutedEventArgs e)
    {

    }

    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {

    }
}