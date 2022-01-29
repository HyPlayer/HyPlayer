#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class AlbumPage : Page, IDisposable
{
    private readonly ObservableCollection<NCSong> songs = new();
    private NCAlbum Album;
    private List<NCArtist> artists = new();
    private int page;

    public AlbumPage()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        songs.Clear();
        Album = null;
        artists = null;
        ImageRect.ImageSource = null;
        GC.SuppressFinalize(this);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await Task.Run(() =>
        {
            Common.Invoke(async () =>
            {
                JObject json;
                var albumid = "";
                if (e.Parameter is NCAlbum)
                {
                    Album = (NCAlbum)e.Parameter;
                    albumid = Album.id;
                }
                else if (e.Parameter is string)
                {
                    albumid = e.Parameter.ToString();
                }

                try
                {
                    json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Album,
                        new Dictionary<string, object> { { "id", albumid } });
                    Album = NCAlbum.CreateFromJson(json["album"]);
                    ImageRect.ImageSource =
                        new BitmapImage(
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
                    TextBlockDesc.Text = (json["album"]["alias"].HasValues
                                             ? string.Join(" / ",
                                                   json["album"]["alias"].ToArray().Select(t => t.ToString())) +
                                               "\r\n"
                                             : "")
                                         + json["album"]["description"];
                    var idx = 0;
                    SongContainer.ListSource = "al" + Album.id;
                    foreach (var song in json["songs"].ToArray())
                    {
                        var ncSong = NCSong.CreateFromJson(song);
                        ncSong.Order = idx++;
                        songs.Add(ncSong);
                    }
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
            });
        });
    }


    private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            Common.Invoke(() =>
            {
                try
                {
                    HyPlayList.AppendNcSongs(songs);

                    HyPlayList.SongAppendDone();

                    HyPlayList.SongMoveTo(0);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
            });
        });
    }


    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        DownloadManager.AddDownload(songs);
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        Common.NavigatePage(typeof(Comments), "al" + Album.id);
    }

    private async void TextBoxAuthor_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (artists.Count > 1)
            await new ArtistSelectDialog(artists).ShowAsync();
        else
            Common.NavigatePage(typeof(ArtistPage), artists[0].id);
    }
}