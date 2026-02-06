#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class AlbumPage : Page
{
    private NCAlbum Album;
    private string albumid;
    private readonly CollectionViewSource AlbumSongsViewSource = new() { IsSourceGrouped = true };
    private List<NCArtist> artists = new();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _albumDynamicLoaderTask;
    private Task _albumInfoLoaderTask;

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
                albumid = Album.Id;
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
        _cancellationTokenSource.Dispose();
    }

    private async Task LoadAlbumDynamic()
    {
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
                image.UriSource = new Uri(Album.Cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER);
            }

            TextBoxAlbumName.Text = Album.Name;

            artists = rst.Album.Artists?.Select(t => t.MapToNcArtist()).ToList();
            TextBoxAuthor.Content = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
            var converter = new DateConverter();
            TextBlockPublishTime.Text = converter.Convert(rst.Album.PublishTime, null, null, null).ToString();
            TextBlockDesc.Text = (string.Join(" / ", rst.Album.Alias) + rst.Album.Alias != null
                ? "\r\n"
                : string.Empty) + rst.Album.Description;
            var idx = 0;
            SongContainer.ListSource = "al" + Album.Id;

            AlbumSongsViewSource.Source = rst.Songs?.Select(song =>
                {
                    return new NCAlbumSong
                    {
                        Album = song.Album.MapToNcAlbum(),
                        Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
                        Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                                     .ToList() ??
                                 [],
                        DiscName = song.CdName,
                        CDName = song.CdName,
                        IsCloud = song.Sid is not "0",
                        IsVip = song.Fee is 1,
                        LengthInMilliseconds = song.Duration,
                        MVId = song.MvId,
                        SongId = song.Id,
                        Order = ++idx,
                        SongName = song.Name,
                        TrackId = song.TrackNumber,
                        TranslatedName = song.Translations is not null ? string.Join(",", song.Translations) : null,
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
        try
        {
            HyPlayList.RemoveAllSong();
            await HyPlayList.AppendNcSource("al" + Album.Id);
            HyPlayList.PlaySourceId = "al" + Album.Id;
            HyPlayList.SongMoveTo(HyPlayList.List.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        var songs = new List<NCSong>();
        foreach (var discSongs in (IEnumerable<DiscSongs>)AlbumSongsViewSource.Source) songs.AddRange(discSongs);

        DownloadManager.AddDownload(songs);
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Comments), "al" + Album.Id);
    }

    private async void TextBoxAuthor_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (artists.Count > 1)
            await new ArtistSelectDialog(artists).ShowAsync();
        else
            Common.NavigatePage(typeof(ArtistPage), artists[0].Id);
    }

    private void BtnSub_Click(object sender, RoutedEventArgs e)
    {
        _ = Common.NeteaseAPI?.RequestAsync(NeteaseApis.AlbumSubscribeApi,
            new AlbumSubscribeRequest() { Id = albumid, IsSubscribe = BtnSub.IsChecked ?? false });
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        await HyPlayList.AppendNcSource("al" + Album.Id);
    }
}

public partial class DiscSongs : List<NCAlbumSong>
{
    public DiscSongs(IEnumerable<NCAlbumSong> items) : base(items)
    {
    }

    public object Key { get; set; }
}