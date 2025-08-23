#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Home : Page, IDisposable
{
    private static List<string> RandomSlogen = new()
    {
        "用音乐开启新的一天吧",
        "戴上耳机 享受新的一天吧"
    };

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _rankListLoaderTask;

    private bool disposedValue = false;

    public Home()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (Common.Logined)
            LoadLoginedContent();
        else LoadUnLoginedContent();
        HyPlayList.OnLoginDone += LoadLoginedContent;
    }

    private void LoadUnLoginedContent()
    {
        _rankListLoaderTask = LoadRanklist();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        HyPlayList.OnLoginDone -= LoadLoginedContent;
        UnLoginedContent.Children.Clear();
        //DailySongContainer.Children.Clear();
        RankPlayList.Children.Clear();
        //MySongHis.Children.Clear();
        RecommendSongListContainer.Children.Clear();
        if (_rankListLoaderTask != null && !_rankListLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _rankListLoaderTask;
            }
            catch
            {
                Dispose();
                return;
            }
        }

        Dispose();
    }

    private async void LoadLoginedContent()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        _ = Common.Invoke(() =>
        {
            _cancellationToken.ThrowIfCancellationRequested();
            UnLoginedContent.Visibility = Visibility.Collapsed;
            LoginedContent.Visibility = Visibility.Visible;
            TbHelloUserName.Text = Common.LoginedUser?.name ?? string.Empty;
            UserImageRect.ImageSource = Common.Setting.noImage
                ? null
                : new BitmapImage(new Uri(Common.LoginedUser?.avatar, UriKind.RelativeOrAbsolute));
        });
        //我们直接Batch吧
        try
        {
            var ret = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Toplist, "ranklist", async () =>
            {
                var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ToplistApi, _cancellationToken);
                if (resp.IsError)
                {
                    Common.AddToTeachingTipLists("加载榜单出错", resp.Error?.Message);
                }

                return resp.Value;
            });


            _ = Common.Invoke(() =>
            {
                _cancellationToken.ThrowIfCancellationRequested();
                RankPlayList.Children.Clear();
                foreach (var bditem in ret?.List ?? [])
                    RankPlayList.Children.Add(new PlaylistItem(bditem.MapToNCPlayList()));
            });


            //推荐歌单加载部分 - 优先级稍微靠后下
            try
            {
                var ret1 = await Common.NeteaseAPI.RequestAsync(NeteaseApis.RecommendResourceApi, _cancellationToken);
                if (ret1.IsError)
                {
                    Common.AddToTeachingTipLists("加载推荐歌单出错", ret1.Error.Message);
                }
                else
                {
                    _ = Common.Invoke(() =>
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        RecommendSongListContainer.Children.Clear();
                        foreach (var item in ret1.Value?.Recommends ?? [])
                            RecommendSongListContainer.Children.Add(new PlaylistItem(item.MapToNCPlayList()));
                    });
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    public async Task LoadRanklist()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Toplist, "ranklist", async () =>
            {
                var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ToplistApi, _cancellationToken);
                if (resp.IsError)
                {
                    Common.AddToTeachingTipLists("加载榜单出错", resp.Error?.Message);
                }

                return resp.Value;
            });

            foreach (var PlaylistItemJson in json.List ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncp = PlaylistItemJson.MapToNCPlayList();
                RankList.Children.Add(new PlaylistItem(ncp));
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        PersonalFM.InitPersonalFM();
    }

    private void dailyRcmTapped(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
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
    }

    private void FMTapped(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        PersonalFM.InitPersonalFM();
    }

    private void LikedSongListTapped(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
    }

    private void HeartBeatTapped(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        _ = Api.EnterIntelligencePlay(_cancellationToken);
    }

    private void UserTapped(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Home));
        Common.NavigatePage(typeof(Me), null, null);
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                RecommendSongListContainer.Children.Clear();
                MainContainer.Children.Clear();
                UnLoginedContent.Children.Clear();
                RankList.Children.Clear();
                RankPlayList.Children.Clear();
                _cancellationTokenSource.Dispose();
            }

            HyPlayList.OnLoginDone -= LoadLoginedContent;
            disposedValue = true;
        }
    }

    ~Home()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}