#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public bool IsDisposed = false;

    public Home()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }
    ~Home()
    {
        Dispose(true);
    }

    public void Dispose()
    {
        Dispose(false);
    }
    private void Dispose(bool isFinalizer)
    {
        if (IsDisposed) return;
        RecommendSongListContainer.Children.Clear();
        MainContainer.Children.Clear();
        UnLoginedContent.Children.Clear();
        RankList.Children.Clear();
        RankPlayList.Children.Clear();
        _cancellationTokenSource.Dispose();
        IsDisposed = true;
        if (!isFinalizer) GC.SuppressFinalize(this);
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
        _ = Common.Invoke(() =>
        {
            _cancellationToken.ThrowIfCancellationRequested();
            UnLoginedContent.Visibility = Visibility.Collapsed;
            LoginedContent.Visibility = Visibility.Visible;
            TbHelloUserName.Text = Common.LoginedUser?.name ?? string.Empty;
            UserImageRect.ImageSource = Common.Setting.noImage
    ? null
    : new BitmapImage(new Uri(Common.LoginedUser.avatar, UriKind.RelativeOrAbsolute));

        });
        //我们直接Batch吧
        try
        {
            var ret = await Common.ncapi.RequestAsync(
                CloudMusicApiProviders.Batch,
                new Dictionary<string, object>
                {
                    { "/api/toplist", "{}" }
                    //{ "/weapi/v1/discovery/recommend/resource","{}" }   //这个走不了Batch
                }
            );

            //每日推荐加载部分 - 日推不加载
            /*
            var rcmdSongsJson = ret["/api/v3/discovery/recommend/songs"]["data"]["dailySongs"].ToArray();
            Common.ListedSongs.Clear();
            DailySongContainer.Children.Clear();
            var NowSongPanel = new StackPanel();
            for (var c = 0; c < rcmdSongsJson.Length; c++)
            {
                if (c % 3 == 0)
                {
                    NowSongPanel = new StackPanel
                    { Orientation = Orientation.Vertical, Height = DailySongContainer.Height, Width = 600 };
                    DailySongContainer.Children.Add(NowSongPanel);
                }

                var nownc = NCSong.CreateFromJson(rcmdSongsJson[c]);
                Common.ListedSongs.Add(nownc);
                NowSongPanel.Children.Add(new SingleNCSong(nownc, c, true, true,
                    rcmdSongsJson[c]["reason"].ToString()));
            }
            */

            //榜单
            _ = Common.Invoke(() =>
            {
                _cancellationToken.ThrowIfCancellationRequested();
                RankPlayList.Children.Clear();
                foreach (var bditem in ret["/api/toplist"]["list"])
                    RankPlayList.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(bditem)));
            });

            //推荐歌单加载部分 - 优先级稍微靠后下
            try
            {
                var ret1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendResource);
                _ = Common.Invoke(() =>
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    RecommendSongListContainer.Children.Clear();
                    foreach (var item in ret1["recommend"])
                        RecommendSongListContainer.Children.Add(new PlaylistItem(NCPlayList.CreateFromJson(item)));
                });
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Toplist);

            foreach (var PlaylistItemJson in json["list"].ToArray())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
        PersonalFM.InitPersonalFM();
    }

    private void LikedSongListTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
        Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
    }

    private async void HeartBeatTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
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

    private void UserTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Home));
        Common.NavigatePage(typeof(Me), null, null);
    }
}