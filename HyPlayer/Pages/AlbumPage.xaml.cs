#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
public sealed partial class AlbumPage : Page, IDisposable
{
    private readonly ObservableCollection<NCSong> AlbumSongs = new();
    private NCAlbum Album;
    private string albumid;
    private readonly CollectionViewSource AlbumSongsViewSource = new() { IsSourceGrouped = true };
    private List<NCArtist> artists = new();
    private int page;
    public bool IsDisposed = false;

    public AlbumPage()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        AlbumSongs.Clear();
        AlbumSongsViewSource.Source = null;
        SongContainer.Dispose();
        albumid = null;
        Album = null;
        artists = null;
        ImageRect.ImageSource = null;
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        JObject json;
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

        _ = LoadAlbumInfo();
        _ = LoadAlbumDynamic();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
    }

    private async Task LoadAlbumDynamic()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.AlbumDetailDynamic,
            new Dictionary<string, object> { { "id", albumid } });
        BtnSub.IsChecked = json["isSub"].ToObject<bool>();
    }

    private async Task LoadAlbumInfo()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        JObject json;
        try
        {
            json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Album,
                new Dictionary<string, object> { { "id", albumid } });
            Album = NCAlbum.CreateFromJson(json["album"]);
            ImageRect.ImageSource =
                Common.Setting.noImage
                    ? null
                    : new BitmapImage(
                        new Uri(Album.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
            TextBoxAlbumName.Text = Album.name;

            TextBoxAlbumName.Text = json["album"]["name"].ToString();
            artists = json["album"]["artists"].ToArray().Select(t => new NCArtist
            {
                avatar = t["picUrl"].ToString(),
                id = t["id"].ToString(),
                name = t["name"].ToString()
            }).ToList();
            TextBoxAuthor.Content = string.Join(" / ", artists.Select(t => t.name));
            var converter = new DateConverter();
            TextBlockPublishTime.Text = converter.Convert((long)json["album"]["publishTime"], null, null, null).ToString();
            TextBlockDesc.Text = (json["album"]["alias"].HasValues
                                     ? string.Join(" / ",
                                           json["album"]["alias"].ToArray().Select(t => t.ToString())) +
                                       "\r\n"
                                     : "")
                                 + json["album"]["description"];
            var idx = 0;
            SongContainer.ListSource = "al" + Album.id;
            /*
            foreach (var song in json["songs"].ToArray())
            {
                var ncSong = NCSong.CreateFromJson(song);
                ncSong.Order = idx++;
                AlbumSongs.Add(ncSong);
            }
            */
            AlbumSongsViewSource.Source = json["songs"].ToArray().Select(jsonSong => new NCAlbumSong
            {
                Album = Album,
                alias = string.Join(" / ", jsonSong["alia"].ToArray().Select(t => t.ToString())),
                Artist = jsonSong["ar"].Select(NCArtist.CreateFromJson).ToList(),
                IsAvailable = jsonSong["privilege"]["st"].ToString() == "0",
                IsVip = jsonSong["fee"]?.ToString() == "1",
                LengthInMilliseconds = double.Parse(jsonSong["dt"].ToString()),
                mvid = jsonSong["mv"]?.ToObject<int>() ?? -1,
                Order = ++idx,
                sid = jsonSong["id"].ToString(),
                songname = jsonSong["name"].ToString(),
                transname = string.Join(" / ",
                        jsonSong["tns"]?.ToArray().Select(t => t.ToString()) ?? Array.Empty<string>()),
                Type = HyPlayItemType.Netease,
                IsCloud = false,
                CDName = jsonSong["cd"].ToString(),
                DiscName = jsonSong["cd"].ToString(),
                TrackId = jsonSong["no"].ToObject<int>()
            }).ToList().GroupBy(t => t.DiscName).OrderBy(t => t.Key)
                .Select(t => new DiscSongs(t) { Key = t.Key });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        try
        {
            HyPlayList.RemoveAllSong();
            await HyPlayList.AppendNcSource("al" + Album.id);
            HyPlayList.SongAppendDone();
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        var songs = new List<NCSong>();
        foreach (var discSongs in (IEnumerable<DiscSongs>)AlbumSongsViewSource.Source) songs.AddRange(discSongs);

        DownloadManager.AddDownload(songs);
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        Common.NavigatePage(typeof(Comments), "al" + Album.id);
    }

    private async void TextBoxAuthor_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        if (artists.Count > 1)
            await new ArtistSelectDialog(artists).ShowAsync();
        else
            Common.NavigatePage(typeof(ArtistPage), artists[0].id);
    }

    private void BtnSub_Click(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.AlbumSubscribe,
            new Dictionary<string, object>
                { { "id", albumid }, { "t", BtnSub.IsChecked.GetValueOrDefault(false) ? "1" : "0" } });
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AlbumPage));
        await HyPlayList.AppendNcSource("al" + Album.id);
        HyPlayList.SongAppendDone();
    }
}

public class DiscSongs : List<NCAlbumSong>
{
    public DiscSongs(IEnumerable<NCAlbumSong> items) : base(items)
    {
    }

    public object Key { get; set; }
}