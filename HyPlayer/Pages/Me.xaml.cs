#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.User;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Me : Page
{
    private readonly ObservableCollection<SimpleListItem> likedPlayList = new();
    private readonly ObservableCollection<SimpleListItem> myPlayList = new();
    private string uid = "";
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadPlaylistTask;
    private Task _loadUserTask;
    private UserDisplay userDisplay;

    public Me()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if ((_loadPlaylistTask != null && !_loadPlaylistTask.IsCompleted)
            || (_loadUserTask != null && !_loadUserTask.IsCompleted))
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadPlaylistTask;
                await _loadUserTask;
            }
            catch
            {
                //Ignore
            }
        }
        _cancellationTokenSource.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        userDisplay = new(Common.LoginedUser);
        if (e.Parameter != null)
        {
            uid = (string)e.Parameter;
            ButtonLogout.Visibility = Visibility.Collapsed;
        }
        else
        {
            uid = Common.LoginedUser.Id;
        }
        _loadUserTask = LoadUser();
        _loadPlaylistTask = LoadPlayList();
    }
    public async Task LoadUser()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserDetail, uid, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.UserDetailApi,
                    new UserDetailRequest()
                    {
                        UserId = uid
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("用户信息获取失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            });
            NCUser currentUser = resp?.Profile?.MapToNcUser();
            userDisplay = new(currentUser);
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
        finally
        {
            Bindings.Update();
        }
    }
    public async Task LoadPlayList()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var val = await SimpleCacher.GetOrCreateCacheAsync(CacheType.UserPlaylist, uid, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.UserPlaylistApi,
                    new UserPlaylistRequest()
                    {
                        Uid = uid,
                        Limit = 1000 // 为什么这么大, 官方客户端也是这么大
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("用户歌单获取失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            });

            var subListIdx = 0;
            foreach (var valuePlaylist in val?.Playlists ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var playList = valuePlaylist.MapToNCPlayList();
                if (playList.Creator.Id != uid)
                {
                    likedPlayList.Add(
                        new SimpleListItem
                        {
                            CoverLink = playList.Cover,
                            LineOne = playList.Creator.Name,
                            LineThree = $"播放量: {playList.PlayCount} | 歌曲数: {playList.TrackCount}",
                            LineTwo = playList.Description,
                            Order = subListIdx++,
                            ResourceId = "pl" + playList.PlaylistId,
                            Title = playList.Name,
                            CanPlay = true
                        }
                    );
                }
                else
                {
                    myPlayList.Add(
                        new SimpleListItem
                        {
                            CoverLink = playList.Cover,
                            LineOne = playList.Creator.Name,
                            LineThree = $"播放量: {playList.PlayCount} | 歌曲数: {playList.TrackCount}",
                            LineTwo = playList.Description,
                            Order = subListIdx++,
                            ResourceId = "pl" + playList.PlaylistId,
                            Title = playList.Name,
                            CanPlay = true
                        }
                    );
                }
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void Logout_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Common.Logined = false;
            Common.LoginedUser = new NCUser();
            if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("Cookies", out var container))
            {
                container.Values.Clear();
            }
            Common.NeteaseAPI.Option.Cookies.Clear();
            Common.Setting.SaveCookies();
            Common.PageMain.MainFrame.Navigate(typeof(BasePage));
            _ = SimpleCacher.ClearCacheAsync(CacheType.Login);
            _ = ((App)Application.Current).InitializeJumpList();
        }
        catch
        {
        }
    }

    private async void BtnPlayClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.RemoveAllSong();
        await HyPlayList.AppendNcSource(((Button)sender).Tag.ToString());
        if (((Button)sender).Tag.ToString().Substring(0, 2) == "pl" ||
            ((Button)sender).Tag.ToString().Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ((Button)sender).Tag.ToString().Substring(2);

        HyPlayList.NowPlaying = -1;
        HyPlayList.SongMoveNext();
    }

    private void SongListItemClicked(object sender, TappedRoutedEventArgs e)
    {
        _ = Common.NavigatePageResource(((Grid)sender).Tag.ToString());
    }

    private void RectangleImage_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Common.Setting.IsOldThemeEnabled = false;
        Common.AddToTeachingTipLists("已重置, 请重启");
    }
}
