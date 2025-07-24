#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class SongListDetail : Page, IDisposable
{
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        "IsLoading", typeof(bool), typeof(SongListDetail), new PropertyMetadata(true));

    private int page;
    public NCPlayList playList;
    public ObservableCollection<NCSong> Songs;
    private bool disposedValue = false;
    private DataTransferManager _dataTransferManager = DataTransferManager.GetForCurrentView();
    private Task _songListLoaderTask;
    private Task _songListColorLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public SongListDetail()
    {
        InitializeComponent();
        Songs = new ObservableCollection<NCSong>();
        _dataTransferManager.DataRequested += DataTransferManagerOnDataRequested;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    private void DataTransferManagerOnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dp = new DataPackage();
        dp.Properties.Title = playList.name;
        dp.SetWebLink(new Uri("https://music.163.com/#/playlist?id=" +
                              playList.plid));
        var request = args.Request;
        request.Data = dp;
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public async Task LoadAlbumImage()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        using var result = await Common.HttpClient!.GetAsync(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER), _cancellationToken);
        if (result.IsSuccessStatusCode)
        {
            using var stream = await result.Content.ReadAsStreamAsync();
            using var inputStream = stream.AsRandomAccessStream();
            _cancellationToken.ThrowIfCancellationRequested();
            Color imageMainColor = await ColorExtractor.ExtractColorFromStream(inputStream);
            if (AlbumColor != null)
            {
                _ = Common.Invoke(() => AlbumColor.Color = imageMainColor);
            }
        }
    }

    public void LoadSongListDetail()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        if (Common.Setting.noImage)
        {
            AlbumImageSource = null;
        }
        else
        {
            AlbumImageSource.UriSource = new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
            _ = Task.Run(LoadAlbumImage);
        }

        TextBoxPLName.Text = playList.name;
        DescriptionTextBlock.Text = playList.desc;
        TextBoxAuthor.Content = playList.creater.name;
        ButtonLike.Tag = playList.subscribed;
        UpdateLikeBtnStyle();
        if (playList.updateTime.Year != 0001)
            TextBoxUpdateTime.Text = $"{DateConverter.FriendFormat(playList.updateTime)}更新";
    }
    public void UpdateLikeBtnStyle()
    {
        if ((bool)ButtonLike.Tag == true)
        {
            LikedIcon.Glyph = "\uE10B";
            LikeBtnText.Text = "已收藏";
        }
        else
        {
            LikedIcon.Glyph = "\uE0B4";
            LikeBtnText.Text = "收藏";
        }

    }


    public async Task LoadSongListItem()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        IsLoading = true;
        if (playList.plid != "-666")
        {
            await LoadPlayListItems();
            await LoadPage();
        }
        else
        {
            ButtonIntel.Visibility = Visibility.Collapsed;
            await LoadDailyRcmdItems();
        }

        IsLoading = false;
    }

    private async Task LoadDailyRcmdItems()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        SongsList.ListSource = "content";
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var items = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "recommendSongs", async () =>
            {
                // 每天推荐歌曲
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.RecommendSongsApi, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载日推出错", json.Error?.Message);
                    return null;
                }
                return json.Value;
            }, TimeSpan.FromDays(1));
            
            if (items?.Data?.DailySongs?.FirstOrDefault()?.RecommendReason == "birthDaySong")
            {
                // 诶呀,没想到还过生了,吼吼
                DescriptionTextBlock.Text = "生日快乐~ 今天也要开心哦!";
                DescriptionTextBlock.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
                DescriptionTextBlock.FontSize = 25;
            }

            var idx = 0;
            foreach (var song in items?.Data?.DailySongs ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncSong = song.MapNcSong();
                ncSong.IsAvailable = true;
                ncSong.Order = idx++;
                Songs.Add(ncSong);
            }
            NextPage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private List<string> _songListIds = [];

    private async Task LoadPlayListItems()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracks, playList.plid, async () =>
            {
                // 歌单详情
                var rst = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                    new PlaylistTracksGetRequest()
                    {
                        Id = playList.plid
                    }, _cancellationToken);
                if (rst.IsError)
                {
                    Common.AddToTeachingTipLists("加载歌单出错", rst.Error?.Message ?? "未知错误");
                    return null;
                }
                return rst.Value;
            });


            

            var playlistDetail = json?.Playlist?.TrackIds;
            if (playlistDetail is null)
            {
                Common.AddToTeachingTipLists("加载歌单出错", "未找到歌单信息");
                return;
            }
            if (json.Playlist.SpecialType == 5 &&
                json.Playlist.Creator?.UserId == Common.LoginedUser?.id)
            {
                ButtonIntel.Visibility = Visibility.Visible;
                SongsList.IsMySongList = true;
            }
            _songListIds = playlistDetail.Select(x => x.Id).ToList();
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadPage()
    {

        var trackIds = _songListIds.Skip(page * 500).Take(500).ToList();

        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracksDetail, playList.plid + "_" + page, async () =>
            {
                // 歌单歌曲详情
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                    new SongDetailRequest()
                    {
                        IdList = trackIds
                    }, _cancellationToken);
                if (json is { IsError: true, Error.ErrorCode: 405 })
                {
                    treashold = ++cooldownTime * 10;
                    page--;
                    Common.AddToTeachingTipLists("贪婪加载被风控", $"渐进加载速度过于快, 将在 {cooldownTime * 10} 秒后尝试继续加载, 正在清洗请求");
                    return null;
                }
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载歌单歌曲出错", json.Error?.Message ?? "未知错误");
                    return null;
                }
                return json.Value;
            });
            
            if (rst is null)
            {
                return;
            }
            var privileges = rst.Privileges;
            var idx = page * 500;
            var i = 0;
            foreach (var jToken in rst.Songs ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncSong = jToken.MapToNcSong();
                ncSong.IsAvailable = privileges?[i++].St is 0;
                ncSong.Order = idx++;
                Songs.Add(ncSong);
            }
            if (_songListIds.Count <= Songs.Count)
            {
                NextPage.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        Dispose();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter != null)
        {
            if (e.Parameter is NCPlayList)
            {
                playList = (NCPlayList)e.Parameter;
            }
            else
            {
                var pid = e.Parameter.ToString();

                try
                {
                    var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistDetail, pid, async () =>
                    {
                        // 歌单详情
                        var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistDetailApi,
                            new PlaylistDetailRequest()
                            {
                                Id = pid
                            }, _cancellationToken);
                        if (json.IsError)
                        {
                            Common.AddToTeachingTipLists("加载歌单出错", json.Error?.Message ?? "未��错误");
                            return null;
                        }
                        return json.Value;
                    });
                    
                    playList = rst?.Playlists?.FirstOrDefault().MapToNCPlayList();
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
            }
        }

        SongsList.ListSource = "pl" + playList?.plid;
        LoadSongListDetail();
        _songListLoaderTask = LoadSongListItem();
        if (Common.Setting.greedlyLoadPlayContainerItems)
            HyPlayList.OnTimerTicked += GreedlyLoad;
    }

    int treashold = 3;
    int cooldownTime = 0;

    private void GreedlyLoad()
    {
        _ = Common.Invoke(() =>
        {
            if (treashold > 10)
            {
                treashold--;
                return;
            }
            if (Songs.Count > 0 && NextPage.Visibility == Visibility.Visible && treashold-- <= 0 && !disposedValue)
            {
                NextPage_OnClickPage_OnClick(null, null);
                treashold = 3;
            }
            else if (Songs.Count > 0 && NextPage.Visibility == Visibility.Collapsed || disposedValue)
            {
                HyPlayList.OnTimerTicked -= GreedlyLoad;
            }
        });
    }


    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        if (playList.plid != "-666")
        {
            HyPlayList.RemoveAllSong();
            await HyPlayList.AppendPlayList(playList.plid);
            HyPlayList.PlaySourceId = playList.plid;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }
        else
        {
            HyPlayList.AppendNcSongs(Songs.ToList());
            HyPlayList.PlaySourceId = playList.plid;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }
    }


    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        page++;
        _songListLoaderTask = LoadPage();
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        Common.NavigatePage(typeof(Comments), "pl" + playList.plid);
    }

    private void ButtonHeartBeat_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        _ = Api.EnterIntelligencePlay(_cancellationToken);
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        DownloadManager.AddDownload(Songs.ToList());
    }

    private async void LikeBtnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        var result = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistSubscribeApi,
            new PlaylistSubscribeRequest()
            {
                PlaylistId = playList.plid,
                IsSubscribe = !playList.subscribed
            }, _cancellationToken);
        if (result.IsError)
        {
            Common.AddToTeachingTipLists("操作失败", result.Error.Message);
            return;
        }
        playList.subscribed = !playList.subscribed;
        ButtonLike.Tag = playList.subscribed;
        UpdateLikeBtnStyle();
    }

    private void TextBoxAuthor_Tapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        Common.NavigatePage(typeof(Me), playList.creater.id);
    }

    private void BtnShare_Clicked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        DataTransferManager.ShowShareUI();
    }


    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        if (playList.plid != "-666")
            await HyPlayList.AppendPlayList(playList.plid);
        else
            HyPlayList.AppendNcSongs(Songs.ToList());
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Songs.Clear();
                playList = null;
                AlbumImageSource = null;
                _cancellationTokenSource.Dispose();
            }
            SongsList.Dispose();
            _dataTransferManager.DataRequested -= DataTransferManagerOnDataRequested;
            disposedValue = true;
        }
    }

    ~SongListDetail()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void BtnComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(SongListDetail));
        Common.NavigatePage(typeof(Comments), "pl" + playList.plid);
    }
}