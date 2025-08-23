#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ArtistPage : Page, IDisposable
{
    public static readonly DependencyProperty SongHasMoreProperty = DependencyProperty.Register(
        "SongHasMore", typeof(bool), typeof(ArtistPage), new PropertyMetadata(default(bool)));

    private readonly ObservableCollection<NCSong> allSongs = new();
    private readonly ObservableCollection<NCSong> hotSongs = new();
    private NCArtist artist;
    private int page;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _hotSongsLoaderTask;
    private Task _albumLoaderTask;
    private Task _songsLoaderTask;
    private bool disposedValue = false;

    public ArtistPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public bool SongHasMore
    {
        get => (bool)GetValue(SongHasMoreProperty);
        set => SetValue(SongHasMoreProperty, value);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
            var artistId = e.Parameter as string;
            if (artistId is null)
            {
                Common.AddToTeachingTipLists("艺人ID为空", "请检查传入的参数是否正确");
                return;
            }
            var res = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistDetail, artistId, async () =>
            {
                var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistDetailApi,
                    new ArtistDetailRequest() { ArtistId = artistId }, _cancellationToken);
                if (resp.IsError && resp.Error?.ErrorCode.ToString() == "404")
                {
                    Common.AddToTeachingTipLists("艺人不存在", null);
                    return null;
                }
                if (resp.IsError)
                {
                    Common.AddToTeachingTipLists("获取艺人信息失败", resp.Error?.Message);
                    return null;
                }
                
                return resp.Value;
            });

            if (res is null)
            {
                return;
            }

            artist = res?.Artist.MapToNcArtist();
            if (res?.Artist?.PicUrl?.StartsWith("http") is true)
            {
                if (Common.Setting.noImage)
                {
                    ImageRect.ImageSource = ImageRect1.ImageSource = null;
                }

                BitmapImage image = new BitmapImage();
                ImageRect.ImageSource = ImageRect1.ImageSource = image;
                image.UriSource = new Uri(res.Artist.PicUrl + "?param=" +
                                          StaticSource.PICSIZE_ARTIST_DETAIL_COVER);
            }

            TextBoxArtistName.Text = res?.Artist?.Name ?? "未知歌手";
            if (res?.Artist?.TransNames != null)
                TextboxArtistNameTranslated.Text =
                    "译名: " + string.Join(",", res.Artist.TransNames);
            else
                TextboxArtistNameTranslated.Visibility = Visibility.Collapsed;
            TextBlockDesc.Text = res.Artist.BriefDesc;
            TextBlockInfo.Text = "歌曲数: " + res.Artist.MusicSize + " | 专辑数: " +
                                 res.Artist.AlbumSize + " | 视频数: " +
                                 res.Artist.MvSize;
            HotSongContainer.ListSource = "sh" + artist.id;
            AllSongContainer.ListSource = "content";
            _hotSongsLoaderTask = LoadHotSongs();
            _songsLoaderTask = LoadSongs();
            _albumLoaderTask = LoadAlbum();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_albumLoaderTask != null && !_albumLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _albumLoaderTask;
            }
            catch
            {
            }
        }

        if (_songsLoaderTask != null && !_songsLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _songsLoaderTask;
            }
            catch
            {
            }
        }

        if (_hotSongsLoaderTask != null && !_hotSongsLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _hotSongsLoaderTask;
            }
            catch
            {
            }
        }
    }

    private async Task LoadHotSongs()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var j1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, artist.id, async () =>
            {
                var j1res = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistTopSongApi,
                    new ArtistTopSongRequest() { ArtistId = artist.id }, _cancellationToken);
                if (j1res.IsError)
                {
                    Common.AddToTeachingTipLists("获取歌手热门歌曲失败", j1res.Error?.Message);
                    return null;
                }

                return j1res.Value?.Songs;
            });

            hotSongs.Clear();
            var idx = 0;
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, artist.id, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                    new SongDetailRequest() { IdList = j1?.Select(t => t.Id).ToList() }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("获取歌手热门歌曲失败", json.Error.Message);
                    return null;
                }

                return json.Value;
            });
            if (jv is null)
            {
                return;
            }

            foreach (var item in jv?.Songs ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncSong = item.MapToNcSong();
                ncSong.IsAvailable =
                    jv!.Privileges?[idx].St == 0;
                ncSong.Order = idx++;
                hotSongs.Add(ncSong);
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadSongs()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var j1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, artist.id + "_" + page,
                async () =>
                {
                    var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistSongsApi,
                        new ArtistSongsRequest() { ArtistId = artist.id, Limit = 50, Offset = page * 50 },
                        _cancellationToken);
                    if (resp.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌手热门歌曲失败", resp.Error?.Message);
                        return null;
                    }

                    return resp.Value;
                });
            var idx = 0;


            try
            {
                var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, artist.id + "_" + page,
                    async () =>
                    {
                        var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                            new SongDetailRequest() { IdList = j1.Songs?.Select(t => t.Id).ToList() },
                            _cancellationToken);
                        if (json.IsError)
                        {
                            Common.AddToTeachingTipLists("获取歌手热门歌曲失败", json.Error!.Message);
                            return null;
                        }

                        return json.Value;
                    });
                if (jv is null)
                {
                    return;
                }

                foreach (var item in jv.Songs)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var ncSong = item.MapToNcSong();
                    ncSong.IsAvailable =
                        jv.Privileges?[idx].St == 0;
                    ncSong.Order = page * 50 + idx++;
                    allSongs.Add(ncSong);
                }

                SongHasMore = j1.HasMore;
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

    private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            HyPlayList.AppendNcSongs(hotSongs);
            HyPlayList.SongMoveTo(0);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        page++;
        if (mp.SelectedIndex == 1)
            _songsLoaderTask = LoadSongs();
        else if (mp.SelectedIndex == 2)
            _albumLoaderTask = LoadAlbum();
    }

    private async Task LoadAlbum()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistAlbumsList, artist.id + "_" + page,
                async () =>
                {
                    var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistAlbumsApi,
                        new ArtistAlbumsRequest() { ArtistId = artist.id, Limit = 50, Start = page * 50 }, _cancellationToken);
                    if (resp.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌手专辑失败", resp.Error?.Message);
                        return null;
                    }

                    return resp.Value;
                });

            AlbumContainer.ListItems.Clear();
            var i = 0;
            foreach (var album in jv?.Albums ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                AlbumContainer.ListItems.Add(new SimpleListItem
                {
                    Title = album.Name,
                    LineOne = string.Join("/", album.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = album.Alias != null
                        ? string.Join(" / ", album.Alias)
                        : "",
                    LineThree = album.Paid ? "付费专辑" : "",
                    ResourceId = "al" + album.Id,
                    CoverLink = album.PictureUrl,
                    Order = page * 50 + i++,
                    CanPlay = true
                });
            }

            if (jv?.HasMore is true)
                NextPage.Visibility = Visibility.Visible;
            else
                NextPage.Visibility = Visibility.Collapsed;
            if (page > 0)
                PrevPage.Visibility = Visibility.Visible;
            else
                PrevPage.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        page--;
        if (mp.SelectedIndex == 1)
            _songsLoaderTask = LoadSongs();
        else if (mp.SelectedIndex == 2)
            _albumLoaderTask = LoadAlbum();
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        page = 0;
    }

    private void PivotView_HeaderScrollProgressChanged(object sender, EventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(ArtistPage));
        GridPersonalInformation.Opacity = 1 - PivotView.HeaderScrollProgress * 1.4;
        RectangleImageBack.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        RectangleImageBackAcrylic.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        TextBlockDesc.Opacity = 1 - PivotView.HeaderScrollProgress * 0.8;

        UserScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.8;
        UserScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.8;
        UserInfoScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.6;
        UserInfoScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.6;
        DescScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.4;
        DescScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.4;
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                allSongs.Clear();
                hotSongs.Clear();
                AllSongContainer.Dispose();
                HotSongContainer.Dispose();
                _cancellationTokenSource.Dispose();
                artist = null;
            }

            disposedValue = true;
        }
    }

    ~ArtistPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}