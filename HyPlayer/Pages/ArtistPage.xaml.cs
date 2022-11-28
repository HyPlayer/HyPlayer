﻿#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

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
    public bool IsDisposed = false;

    public ArtistPage()
    {
        InitializeComponent();
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
            var res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistDetail,
                new Dictionary<string, object> { { "id", (string)e.Parameter } });
            artist = NCArtist.CreateFromJson(res["data"]["artist"]);
            if (res["data"]["artist"]["cover"].ToString().StartsWith("http"))
                ImageRect.ImageSource = ImageRect1.ImageSource =
                    Common.Setting.noImage
                        ? null
                        : new BitmapImage(new Uri(res["data"]["artist"]["cover"] + "?param=" +
                                                  StaticSource.PICSIZE_ARTIST_DETAIL_COVER));
            TextBoxArtistName.Text = res["data"]["artist"]["name"].ToString();
            if (res["data"]["artist"]["transNames"].HasValues)
                TextboxArtistNameTranslated.Text =
                    "译名: " + string.Join(",", res["data"]["artist"]["transNames"].ToObject<string[]>());
            else
                TextboxArtistNameTranslated.Visibility = Visibility.Collapsed;
            TextBlockDesc.Text = res["data"]["artist"]["briefDesc"].ToString();
            TextBlockInfo.Text = "歌曲数: " + res["data"]["artist"]["musicSize"] + " | 专辑数: " +
                                 res["data"]["artist"]["albumSize"] + " | 视频数: " +
                                 res["data"]["artist"]["mvSize"];
            HotSongContainer.ListSource = "sh" + artist.id;
            AllSongContainer.ListSource = "content";
            _ = LoadHotSongs();
            _ = LoadSongs();
            _ = LoadAlbum();
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        allSongs.Clear();
        hotSongs.Clear();
        AllSongContainer.Dispose();
        HotSongContainer.Dispose();
        artist = null;
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task LoadHotSongs()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            var j1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistTopSong,
                new Dictionary<string, object> { { "id", artist.id } });

            hotSongs.Clear();
            var idx = 0;
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                new Dictionary<string, object>
                    { ["ids"] = string.Join(",", j1["songs"].ToList().Select(t => t["id"])) }, false);
            foreach (var jToken in json["songs"])
            {
                var ncSong = NCSong.CreateFromJson(jToken);
                ncSong.IsAvailable =
                    json["privileges"][idx][
                        "st"].ToString() == "0";
                ncSong.Order = idx++;
                hotSongs.Add(ncSong);
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadSongs()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            var j1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistSongs,
                new Dictionary<string, object> { { "id", artist.id }, { "limit", 50 }, { "offset", page * 50 } });
            var idx = 0;
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                    new Dictionary<string, object>
                        { ["ids"] = string.Join(",", j1["songs"].ToList().Select(t => t["id"])) });
                foreach (var jToken in json["songs"])
                {
                    var ncSong = NCSong.CreateFromJson(jToken);
                    ncSong.IsAvailable =
                        json["privileges"][idx][
                            "st"].ToString() == "0";
                    ncSong.Order = page * 50 + ++idx;
                    allSongs.Add(ncSong);
                }

                SongHasMore = int.Parse(j1["total"].ToString()) >= (page + 1) * 50;
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

    private void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            HyPlayList.AppendNcSongs(hotSongs);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        page++;
        if (mp.SelectedIndex == 1)
            _ = LoadSongs();
        else if (mp.SelectedIndex == 2)
            _ = LoadAlbum();
    }

    private async Task LoadAlbum()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        try
        {
            var j1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistAlbum,
                new Dictionary<string, object> { { "id", artist.id }, { "limit", 50 }, { "offset", page * 50 } });

            AlbumContainer.ListItems.Clear();
            var i = 0;
            foreach (var albumjson in j1["hotAlbums"].ToArray())
                AlbumContainer.ListItems.Add(new SimpleListItem
                {
                    Title = albumjson["name"].ToString(),
                    LineOne = albumjson["artist"]["name"].ToString(),
                    LineTwo = albumjson["alias"] != null
                        ? string.Join(" / ", albumjson["alias"].ToArray().Select(t => t.ToString()))
                        : "",
                    LineThree = albumjson.Value<bool>("paid") ? "付费专辑" : "",
                    ResourceId = "al" + albumjson["id"],
                    CoverUri = albumjson["picUrl"] + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                    Order = page*50 + i++,
                    CanPlay = true
                });
            if (int.Parse(j1["artist"]["albumSize"].ToString()) >= (page + 1) * 50)
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
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }


    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        page--;
        if (mp.SelectedIndex == 1)
            _ = LoadSongs();
        else if (mp.SelectedIndex == 2)
            _ = LoadAlbum();
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        page = 0;
    }

    private void PivotView_HeaderScrollProgressChanged(object sender, EventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(ArtistPage));
        GridPersonalInformation.Opacity = 1 - PivotView.HeaderScrollProgress * 1.4;
        RectangleImageBack.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        RectangleImageBackAcrylic.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        RectangleImageBack.RadiusX = 10 + PivotView.HeaderScrollProgress * 60;
        RectangleImageBackAcrylic.RadiusX = 10 + PivotView.HeaderScrollProgress * 60;
        RectangleImageBack.RadiusY = 10 + PivotView.HeaderScrollProgress * 60;
        RectangleImageBackAcrylic.RadiusY = 10 + PivotView.HeaderScrollProgress * 60;
        UserScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.8;
        RectangleImage.RadiusX = 10 + PivotView.HeaderScrollProgress * 100;
        RectangleImage.RadiusY = 10 + PivotView.HeaderScrollProgress * 100;
        UserScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.8;
        UserInfoScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.6;
        UserInfoScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.6;
    }
}