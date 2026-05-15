#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Login;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.NeteaseApi.ApiContracts.User;
using HyPlayer.NeteaseApi.ApiContracts.Utils;
using HyPlayer.NeteaseApi;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Microsoft.UI.Xaml.Controls;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WinRT;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

#endregion


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class BasePage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();

    private string nowqrkey;
    private readonly IPlaybackControlService _playback = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();

    public BasePage()
    {
        InitializeComponent();
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        uiState.PageBase = this;
        uiState.GlobalTip = TheTeachingTip;

        if (!_player.PlayerCreated)
        {
            _ = _playback.InitializeAsync();
        }
        WeakReferenceMessenger.Default.Register<TrackChangedMessage>(this, (r, m) => ((BasePage)r).OnChangePlayItem(m.Item));
        WeakReferenceMessenger.Default.Register<CoverChangedMessage>(this, (r, m) => ((BasePage)r).HyPlayList_OnSongCoverChanged(m.Item));

        ApplicationView.TerminateAppOnFinalViewClose = false;
        Ioc.Default.GetRequiredService<INavigationService>().RootFrame = BaseFrame;
        BaseFrame.IsNavigationStackEnabled = !_setting.forceMemoryGarbage;
        Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed -= CoreWindow_PointerPressed;
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        uiState.ClearReferences(this);
        uiState.ClearReferences(TheTeachingTip);
    }

    private async void HyPlayList_OnSongCoverChanged(HyPlayItem playItem)
    {
        await RefreshNavItemCover(playItem);
    }


    private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs args)
    {
        if (args.CurrentPoint.Properties.IsXButton1Pressed)
            if (!CollapseExpandedPlayerIfNeeded())
                _navigation.NavigateBack();
    }

    private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
    {
        if (args.VirtualKey == VirtualKey.GamepadB)
        {
            if (!CollapseExpandedPlayerIfNeeded())
                _navigation.NavigateBack();
            args.Handled = true;
        }

        if (args.VirtualKey == VirtualKey.GamepadY)
            if (_playback.IsPlaying)
                _player.PauseAll();
            else if (!_playback.IsPlaying) _player.PlayAll();

        if (args.VirtualKey == VirtualKey.Escape)
            CollapseExpandedPlayerIfNeeded();
    }

    private bool CollapseExpandedPlayerIfNeeded()
    {
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        if (!uiState.IsExpanded) return false;
        (uiState.BarPlayBar as PlayBar)?.CollapseExpandedPlayer();
        return true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_setting.DisablePopUp)
        {
            var dialog = new ContentDialog
            {
                Title = "重要提示",
                Content = "本软件仅供学习交流使用，下载后请在 24 小时内删除。\r\n请勿使用此软件登录网易云音乐或进行违反网易云音乐用户协议的行为",
                CloseButtonText = "退出软件",
                PrimaryButtonText = "我已知晓",
                IsPrimaryButtonEnabled = true,
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.CloseButtonClick += (_, _) => _ = ApplicationView.GetForCurrentView().TryConsolidateAsync();
            _ = dialog.ShowAsync();
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

    private async Task LoadLoginData()
    {
        try
        {
            if (Setting.LoadCookies() || _api?.Option.AdditionalParameters.Cookies.Count is > 0)
            {
                try
                {
                    await LoginDone();
                }
                catch
                {
                    // ignored
                }
            }
            else
            {
                _navigation.Navigate(typeof(Welcome));
            }
        }
        catch
        {
            // ignored
        }
    }

    private async void ButtonLogin_OnClick(object sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(TextBoxAccount.Text) || string.IsNullOrWhiteSpace(TextBoxPassword.Password))
        {
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Message = "用户名或密码不能为空";
            return;
        }

        DialogLogin.IsPrimaryButtonEnabled = false;
        DialogLogin.PrimaryButtonText = "登录中......";
        try
        {
            var queries = new Dictionary<string, object>();
            var account = TextBoxAccount.Text;
            var isPhone = IsPhoneRegex().IsMatch(account);
            var contryCode = string.Empty;
            if (account.StartsWith('+'))
            {
                isPhone = true;
                // get the string between '+' and ' '
                contryCode = account[1..account.IndexOf(' ')];
                account = account[(account.IndexOf(' ') + 1)..];
            }
            if (isPhone)
            {

                var response = await _api.RequestAsync(NeteaseApis.LoginCellphoneApi,
                    new LoginCellphoneRequest() { Cellphone = account, CountryCode = string.IsNullOrEmpty(contryCode) ? null : contryCode, Password = TextBoxPassword.Password });
                if (response.IsError)
                {
                    InfoBarLoginHint.IsOpen = true;
                    InfoBarLoginHint.Title = "登录失败";
                    DialogLogin.PrimaryButtonText = "登录";
                    DialogLogin.IsPrimaryButtonEnabled = true;
                    InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                    InfoBarLoginHint.Message = "登录失败 " + response.Error.Message;
                }
                else
                {
                    await SimpleCacher.ClearCacheAsync(CacheType.Login);
                    await LoginDone();
                }
            }
            else
            {
                var response = await _api.RequestAsync(NeteaseApis.LoginEmailApi,
                    new LoginEmailRequest() { Email = account, Password = TextBoxPassword.Password });
                if (response.IsError)
                {
                    InfoBarLoginHint.IsOpen = true;
                    InfoBarLoginHint.Title = "登录失败";
                    DialogLogin.PrimaryButtonText = "登录";
                    DialogLogin.IsPrimaryButtonEnabled = true;
                    InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                    InfoBarLoginHint.Message = "登录失败 " + response.Error.Message;
                }
                else
                {
                    await SimpleCacher.ClearCacheAsync(CacheType.Login);
                    await LoginDone();
                }
            }
        }
        catch (Exception ex)
        {
            DialogLogin.IsPrimaryButtonEnabled = true;
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Severity = InfoBarSeverity.Error;
            InfoBarLoginHint.Message = "登录失败 " + ex;
        }
    }

    private void ButtonCloseLoginForm_Click(object sender, ContentDialogButtonClickEventArgs args)
    {
        DialogLogin.Hide();
        _navigation.NavigateBack();
    }

    public async Task<bool> LoginDone()
    {
        LoginStatusResponse LoginStatus;
        var result = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "userStatus", async () =>
        {
            var result = await _api.RequestAsync(NeteaseApis.LoginStatusApi);
            if (result.IsError)
            {
                _notification.ShowMessage("登录失败", result.Error?.Message);
                return null;
            }
            return result.Value;
        });

        if (result is null)
            return false;

        LoginStatus = result;

        if (LoginStatus.Account == null) return false;
        InfoBarLoginHint.IsOpen = true;
        InfoBarLoginHint.Title = "登录成功";
        //存储Cookie
        Setting.SaveCookies();
        if (LoginStatus.Profile != null)
            _auth.CurrentUser = LoginStatus.Profile.MapToNcUser();
        else
            _auth.CurrentUser = new NCUser
            {
                Avatar = "ms-appx:///Assets/icon.png",
                Id = LoginStatus.Account.Id,
                Name = LoginStatus.Account.UserName,
                Signature = "此账号未进行手机号验证, 请使用网易云音乐客户端登录后再继续操作"
            };

        _auth.IsLoggedIn = true;
        NavItemLogin.Content = _auth.CurrentUser.Name;
        NavItemLogin.Icon = new BitmapIcon
        {
            UriSource = new Uri(_auth.CurrentUser.Avatar + "?param=" +
                                                    StaticSource.PICSIZE_NAVITEM_USERAVATAR),
            ShowAsMonochrome = false
        };
        InfoBarLoginHint.Severity = InfoBarSeverity.Success;
        InfoBarLoginHint.Message = "欢迎 " + _auth.CurrentUser.Name;
        DialogLogin.Hide();
        //加载我喜欢的歌
        _ = LoadMyLikelist();
        _ = LoadSongList();

        // 执行签到操作
        // DoDailySign();

        var authService = Ioc.Default.GetRequiredService<IAuthService>();
        authService.NotifyLoginCompleted();
        App.InitializeJumpList().SafeFireAndForget();
        if (_setting.noImage)
        {
            _navigation.Navigate(typeof(Welcome));
        }
        else
        {
            NavMain.SelectedItem = NavItemLogin;
        }

        return true;
    }

    private static async Task LoadMyLikelist()
    {
        var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        var auth = Ioc.Default.GetRequiredService<IAuthService>();
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var ids = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "likedSongs", async () =>
        {
            var js = await api.RequestAsync(NeteaseApis.LikelistApi, new LikelistRequest() { Uid = auth.CurrentUser!.Id });
            if (js.IsError)
            {
                notification.ShowMessage("获取喜欢列表失败", js.Error?.Message);
                return null;
            }

            return js.Value;
        });

        var likedSongs = ids?.TrackIds?.ToList() ?? [];
        auth.LikedSongs.Clear();
        auth.LikedSongs.AddRange(likedSongs);
    }

    public async Task LoadSongList()
    {
        //加载用户歌单
        var nowitem = NavItemsMyList;
        var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "mySongList", async () =>
        {
            var json = await _api.RequestAsync(NeteaseApis.UserPlaylistApi,
                new UserPlaylistRequest() { Uid = _auth.CurrentUser!.Id });
            if (json.IsError)
            {
                _notification.ShowMessage("获取歌单失败", json.Error?.Message);
                return null;
            }

            return json.Value;
        });

        NavItemsLikeList.MenuItems.Clear();
        NavItemsMyList.MenuItems.Clear();
        NavItemsLikeList.Visibility = Visibility.Visible;
        NavItemsAddPlaylist.Visibility = Visibility.Visible;
        NavItemsMyList.Visibility = Visibility.Visible;
        NavItemsMyLovedPlaylist.Visibility = Visibility.Visible;
        _auth.MySongLists.Clear();
        var isliked = false;
        foreach (var jToken in jv?.Playlists ?? [])
            if (jToken.Subscribed)
            {
                var item = new NavigationViewItem
                {
                    Content = jToken.Name,
                    Tag = "Playlist" + jToken.Id,
                    Icon = new FontIcon
                    {
                        Glyph = "\uE142"
                    }
                };
                NavItemsLikeList.MenuItems.Add(item);
            }
            else
            {
                _auth.MySongLists.Add(jToken.MapToNCPlayList());
                if (!isliked)
                {
                    isliked = true;
                    continue;
                }

                var item = new NavigationViewItem
                {
                    Icon = new FontIcon
                    {
                        Glyph = jToken.Privacy == 0 ? "\uE142" : "\uE72E"
                    },
                    Content = jToken.Name,
                    Tag = "Playlist" + jToken.Id,
                };
                if (jToken.Privacy == 0)
                    item.Icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 211, 39, 100));

                NavItemsMyList.MenuItems.Add(item);
            }

    }

    private async void NavMain_OnSelectionChanged(NavigationView sender,
                                                  NavigationViewSelectionChangedEventArgs args)
    {
        if (Ioc.Default.GetRequiredService<INavigationService>().NavigatingBack) return;
        var nowitem = sender.SelectedItem?.As<NavigationViewItem>();
        if (Ioc.Default.GetRequiredService<INavigationService>().NavigationHistory.Count > 1)
            NavMain.IsBackEnabled = true;
        if (nowitem.Tag is null) return;

        if (nowitem.Tag.ToString() == "PageMe" && !_auth.IsLoggedIn)
        {
            _api?.Option.Cookies.Clear();//清一遍Cookie防止出错
            await DialogPreLoginHint.ShowAsync();
            return;
        }

        if (nowitem.Tag.ToString() == "MusicCloud") _navigation.Navigate(typeof(MusicCloudPage));

        if (nowitem.Tag.ToString() == "DailyRcmd")
            _navigation.Navigate(typeof(SongListDetail), new NCPlayList
            {
                Cover = "ms-appx:/Assets/icon.png",
                Creator = new NCUser
                {
                    Avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
                    Id = "1",
                    Name = "网易云音乐",
                    Signature = "网易云音乐官方账号 "
                },
                IsDailyRecommend = true,
                HasSubscribed = false,
                Name = "每日歌曲推荐",
                Description = "根据你的口味生成，每天6:00更新"
            });

        if (nowitem.Tag.ToString() == "SonglistMyLike")
        {
            _navigation.Navigate(typeof(SongListDetail), _auth.MySongLists[0].PlaylistId);
            return;
        }

        if (nowitem.Tag.ToString().StartsWith("Playlist"))
            _navigation.Navigate(typeof(SongListDetail), nowitem.Tag.ToString()[8..]);

        switch (nowitem.Tag.ToString())
        {
            case "PageMe":
                _navigation.Navigate(typeof(Me), null);
                break;
            case "PageSearch":
                _navigation.Navigate(typeof(Search), null);
                break;
            case "PageHome":
                _navigation.Navigate(typeof(HomePage), null);
                break;
            case "PageSettings":
                _navigation.Navigate(typeof(Settings), null);
                break;
            case "PageLocal":
                _navigation.Navigate(typeof(LocalMusicPage), null);
                break;
            case "PageHistory":
                _navigation.Navigate(typeof(History), null);
                break;
            case "PageFavorite":
                _navigation.Navigate(typeof(PageFavorite), null);
                break;
        }
    }

    // Invoked events of not-for-navigation items can be handled separately.
    // Meanwhile we set "SelectsOnInvoked" property of these items "False" to avoid the navigation pane indicator being set to them.
    private void NavMain_ItemInvoked(NavigationView sender,
                                           NavigationViewItemInvokedEventArgs args)
    {
        var invokedItemTag = (args.InvokedItemContainer?.As<NavigationViewItem>())?.Tag?.ToString();
        if (invokedItemTag is null || invokedItemTag == string.Empty) return;
        switch (invokedItemTag)
        {
            case "SonglistCreate":
                {
                    _ = new CreateSonglistDialog().ShowAsync();
                    break;
                }
            case "PersonalFM":
                {
                    PersonalFM.InitPersonalFM();
                    break;
                }
            case "HeartBeat":
                Api.EnterIntelligencePlay().SafeFireAndForget();
                break;
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
        if ((sender?.As<Pivot>()).SelectedIndex == 1)
            LoadQr(null, null);
        else
            InfoBarLoginHint.Title = "登录代表你同意相关条款";
    }

    private async void LoadQr(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
    {
        try
        {
            // 保持与原逻辑一致：不显式声明 Key 的泛型类型，避免在 UI 层引入额外类型依赖
            var key = await _api.RequestAsync(NeteaseApis.LoginQrCodeUnikeyApi, new LoginQrCodeUnikeyRequest());
            if (key.IsError)
            {
                _notification.ShowMessage("获取UniKey失败", key.Error.Message);
                return;
            }
            await ReFreshQr(key.Value.Unikey);
            nowqrkey = key.Value.Unikey;
            while (!_auth.IsLoggedIn && nowqrkey == key.Value.Unikey)
            {
                var res = await _api.RequestAsync(NeteaseApis.LoginQrCodeCheckApi,
                                                           new LoginQrCodeCheckRequest() { Unikey = key.Value.Unikey });
                if (res.Value.Code == 800)
                {
                    key = await _api.RequestAsync(NeteaseApis.LoginQrCodeUnikeyApi, new LoginQrCodeUnikeyRequest());
                    if (key.IsError)
                    {
                        _notification.ShowMessage("获取UniKey失败", key.Error.Message);
                        return;
                    }
                    await ReFreshQr(key.Value.Unikey);
                }
                else if (res.Value.Code == 801)
                {
                    if (!InfoBarLoginHint.IsOpen)
                    {
                        InfoBarLoginHint.IsOpen = true;
                    }

                    InfoBarLoginHint.Title = "请扫描上方二维码登录";
                }
                else if (res.Value.Code == 803)
                {
                    if (!InfoBarLoginHint.IsOpen)
                    {
                        InfoBarLoginHint.IsOpen = true;
                    }

                    InfoBarLoginHint.Title = "登录成功";
                    DialogLogin.PrimaryButtonText = "登录成功";
                    await SimpleCacher.ClearCacheAsync(CacheType.Login);
                    await LoginDone();
                    break;
                }
                else if (res.Value.Code == 802)
                {
                    if (!InfoBarLoginHint.IsOpen)
                    {
                        InfoBarLoginHint.IsOpen = true;
                    }

                    InfoBarLoginHint.Title = "请在手机上授权登录";
                }
                await Task.Delay(2000);
            }

        }
        catch (Exception e)
        {
            _notification.ShowMessage("加载二维码时发生错误", e.Message);
        }
    }

    private async Task ReFreshQr(string key)
    {
        var QrUri = new Uri("https://music.163.com/login?codekey=" + key);
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
        BaseFrame.Navigate(typeof(ThirdPartyLogin), (sender?.As<Button>()).Tag.ToString());
    }

    private void NavigationViewItem_Tapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
    {
        NavMain.SelectedItem = sender;
    }


    private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _navigation.Navigate(typeof(Search), sender.Text);
    }

    private void SearchAutoSuggestBox_OnSuggestionChosen(AutoSuggestBox sender,
                                                         AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = (string)args.SelectedItem;
    }

    private void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        /*
        try
        {
            var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
                                             new PlaylistPrivacyRequest() { Id = nowplid });
            if (result.IsError)
            {
                _notification.ShowMessage("公开歌单失败", result.Error.Message);
                return;
            }

            _notification.ShowMessage("成功公开歌单");
            _ = LoadSongList();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("公开歌单失败", ex.Message);
        }
        */
    }

    private void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        /*
        try
        {
            var json = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,
                                             new PlaylistDeleteRequest() { Id = nowplid });
            if (json.IsError)
            {
                _notification.ShowMessage("删除失败", json.Error.Message);
                return;
            }
            _notification.ShowMessage("成功删除");
            _ = LoadSongList();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("删除失败", ex.Message);
        }
         */
    }


    private void TheTeachingTip_OnCloseButtonClick(TeachingTip sender, object args)
    {
        Ioc.Default.GetRequiredService<IUIStateService>().TeachingTipList.Clear();
    }


    private void SearchAutoSuggestBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ((AutoSuggestBox)sender).ItemsSource = null;
    }

    private async void SearchAutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        if (string.IsNullOrEmpty(sender.Text))
        {
            sender.ItemsSource = null;
            return;
        }

        var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,
                                                    new SearchSuggestionRequest() { Keyword = sender.Text });
        if (json.IsError)
        {
            _notification.ShowMessage("获取推荐词失败", json.Error.Message);
            return;
        }
        sender.ItemsSource = json.Value.Result.AllMatch?.Select(t => t.Keyword).ToList();
    }

    private void OnChangePlayItem(HyPlayItem item)
    {
        _ = _notification.InvokeOnUIThread(() =>
        {
            if (item != null)
            {
                NavItemSongName.Text = item.Name;
                NavItemArtist.Text = item.ArtistString;
            }
        });
    }

    public async Task RefreshNavItemCover(HyPlayItem playItem)
    {
        if (_state.CoverStream == null) return;
        _ = _notification.InvokeOnUIThread(async () =>
        {
            if (!Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded && !_setting.noImage)
            {
                try
                {
                    if (playItem != _state.NowPlayingItem) return;
                    using var stream = _state.CoverStream.CloneStream();
                    await NavItemImageSource.SetSourceAsync(stream);
                }
                catch
                {
                }
            }
        });
    }

    private async void BaseFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _ = _notification.InvokeOnUIThread(async () =>
            {
                try
                {
                    await Task.Delay(500);
                    NavMain.SelectionChanged -= NavMain_OnSelectionChanged;
                    Bindings.Update();
                    NavMain.SelectionChanged += NavMain_OnSelectionChanged;
                }
                catch
                {
                    // ignored
                }
            }
        );
    }

    private void BtnApiAddParamClick(object sender, RoutedEventArgs e)
    {
        _ = Launcher.LaunchUriAsync(new Uri("https://github.com/HyPlayer/HyPlayer/wiki/%E5%85%B3%E4%BA%8E-%60ApiAdditionalParameter%60"));
        _navigation.Navigate(typeof(TestPage));
    }

    private void ButtonPreLoginPrimary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        DialogPreLoginHint.Hide();
        _ = DialogLogin.ShowAsync();
    }
    private async void BtnCurrentDeviceIdClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // get current device guid
            var deviceInfo = new EasClientDeviceInformation();
            var deviceId = deviceInfo.Id;
            var androidId = deviceId.ToString("N")[..16];
            var imei = deviceId.ToString("N")[16..];
            var rst = await _api.RequestAsync(NeteaseApis.LoginAnnounceDeviceApi, new LoginAnnounceDeviceRequest
            {
                Imei = imei,
                AndroidId = androidId,
                LocalId = null,
                DeviceName = deviceInfo.FriendlyName,
            });
            if (rst.IsError)
            {
                _notification.ShowMessage("设备ID注册失败, 请尝试其他方案", "获取失败: " + rst.Error.Message);
                return;
            }
            _notification.ShowMessage("设备ID注册成功", "临时用户 ID: " + rst.Value.Data?.Id);
            ButtonPreLoginPrimary_Click(null, null);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("设备ID注册失败, 请尝试其他方案", "错误: " + ex.Message);
            return;
        }
    }
    private void AppTitleBar_BackButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _navigation.NavigateBack();
        }
        catch
        {
            //ignore
        }
    }

    private void AppTitleBar_PaneButtonClick(object sender, RoutedEventArgs e)
    {
        if (NavMain.IsPaneOpen)
            NavMain.IsPaneOpen = false;
        else
            NavMain.IsPaneOpen = true;
    }

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex IsPhoneRegex();
}
