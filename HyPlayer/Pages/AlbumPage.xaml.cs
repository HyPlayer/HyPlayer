#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.NeteaseApi.ApiContracts.Album;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class AlbumPage : Page, IDisposable
{
    private readonly ObservableCollection<NCSong> AlbumSongs = new();
    private NCAlbum Album;
    private string albumid;
    private readonly CollectionViewSource AlbumSongsViewSource = new() { IsSourceGrouped = true };
    private List<NCArtist> artists = new();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _albumDynamicLoaderTask;
    private Task _albumInfoLoaderTask;
    private bool disposedValue = false;

    public AlbumPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        switch (e.Parameter)
        {
            case NCAlbum album:
                Album = album;
                albumid = Album.id;
                break;
            case string:
                albumid = e.Parameter.ToString();
                break;
        }

        _albumInfoLoaderTask = LoadAlbumInfo();
        _albumDynamicLoaderTask = LoadAlbumDynamic();
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_albumInfoLoaderTask != null && !_albumInfoLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _albumInfoLoaderTask;
            }
            catch
            {
            }
        }

        if (_albumDynamicLoaderTask != null && !_albumDynamicLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _albumDynamicLoaderTask;
            }
            catch
            {
            }
        }

        Dispose();
    }

    private async Task LoadAlbumDynamic()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        _cancellationToken.ThrowIfCancellationRequested();
        var js = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumDynamic, albumid, async () =>
        {
            var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumDetailDynamicApi,
                new AlbumDetailDynamicRequest() { Id = albumid }, _cancellationToken);
            if (json.IsError)
            {
                Common.AddToTeachingTipLists("获取专辑动态失败", json.Error?.Message);
                return null;
            }

            return json.Value;
        });


        BtnSub.IsChecked = js.IsSub;
    }

    private async Task LoadAlbumInfo()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, albumid, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumApi,
                    new AlbumRequest() { Id = albumid }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("获取专辑信息失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            });
            if (rst?.Album is null)
            {
                return;
            }

            Album = rst.Album.MapToNcAlbum();
            if (Common.Setting.noImage) ImageRect.ImageSource = null;
            else
            {
                BitmapImage image = new BitmapImage();
                ImageRect.ImageSource = image;
                image.UriSource = new Uri(Album.cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
            }

            TextBoxAlbumName.Text = Album.name;

            artists = rst.Album.Artists?.Select(t => t.MapToNcArtist()).ToList();
            TextBoxAuthor.Content = string.Join(" / ", artists?.Select(t => t.name) ?? []);
            var converter = new DateConverter();
            TextBlockPublishTime.Text = converter.Convert(rst.Album.PublishTime, null, null, null).ToString();
            TextBlockDesc.Text = (string.Join(" / ", rst.Album.Alias) + rst.Album.Alias != null
                ? "\r\n"
                : string.Empty) + rst.Album.Description;
            var idx = 0;
            SongContainer.ListSource = "al" + Album.id;

            AlbumSongsViewSource.Source = rst.Songs?.Select(song =>
                {
                    return new NCAlbumSong
                    {
                        Album = song.Album.MapToNcAlbum(),
                        alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
                        Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                                     .ToList() ??
                                 [],
                        DiscName = song.CdName,
                        CDName = song.CdName,
                        IsCloud = song.Sid is not "0",
                        IsVip = song.Fee is 1,
                        LengthInMilliseconds = song.Duration,
                        mvid = song.MvId,
                        sid = song.Id,
                        Order = ++idx,
                        songname = song.Name,
                        TrackId = song.TrackNumber,
                        transname = song.Translations is not null ? string.Join(",", song.Translations) : null,
                        IsAvailable = true,
                        Type = HyPlayItemType.Netease,
                    };
                }).GroupBy(t => t.DiscName).OrderBy(t => t.Key)
                .Select(t => new DiscSongs(t) { Key = t.Key }).ToList();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        try
        {
            HyPlayList.RemoveAllSong();
            await HyPlayList.AppendNcSource("al" + Album.id);
            HyPlayList.PlaySourceId = "al" + Album.id;
            HyPlayList.SongMoveTo(0);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        var songs = new List<NCSong>();
        foreach (var discSongs in (IEnumerable<DiscSongs>)AlbumSongsViewSource.Source) songs.AddRange(discSongs);

        DownloadManager.AddDownload(songs);
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        Common.NavigatePage(typeof(Comments), "al" + Album.id);
    }

    private async void TextBoxAuthor_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        if (artists.Count > 1)
            await new ArtistSelectDialog(artists).ShowAsync();
        else
            Common.NavigatePage(typeof(ArtistPage), artists[0].id);
    }

    private void BtnSub_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        _ = Common.NeteaseAPI?.RequestAsync(NeteaseApis.AlbumSubscribeApi,
            new AlbumSubscribeRequest() { Id = albumid, IsSubscribe = BtnSub.IsChecked ?? false });
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(AlbumPage));
        await HyPlayList.AppendNcSource("al" + Album.id);
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                AlbumSongs.Clear();
                AlbumSongsViewSource.Source = null;
                SongContainer.Dispose();
                albumid = null;
                Album = null;
                artists = null;
                ImageRect.ImageSource = null;
                _cancellationTokenSource.Dispose();
            }

            disposedValue = true;
        }
    }

    ~AlbumPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class DiscSongs : List<NCAlbumSong>
{
    public DiscSongs(IEnumerable<NCAlbumSong> items) : base(items)
    {
    }

    public object Key { get; set; }
}