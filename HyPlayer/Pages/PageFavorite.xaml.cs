#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Pages;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PageFavorite : Page, IDisposable
{
    private int i;
    private int page;
    public bool IsDisposed = false;
    
    public PageFavorite()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        ItemContainer.ListItems.Clear();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
    }

    private void NavView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        page = 0;
        i = 0;
        ItemContainer.ListItems.Clear();
        RealLoad();
    }

    private void RealLoad()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        switch ((NavView.SelectedItem as NavigationViewItem)?.Tag.ToString())
        {
            case "Album":
                _ = LoadAlbumResult();
                break;
            case "Artist":
                _ = LoadArtistResult();
                break;
            case "Radio":
                _ = LoadRadioResult();
                break;
        }
    }

    private async Task LoadRadioResult()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjSublist,
                new Dictionary<string, object>
                {
                    { "offset", page * 25 }
                });
            BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var pljs in json["djRadios"])
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = pljs["name"].ToString(),
                    LineOne = pljs["dj"]["nickname"].ToString(),
                    LineTwo = pljs["desc"].ToString(),
                    LineThree = "最后一个节目: " + pljs["lastProgramName"],
                    ResourceId = "rd" + pljs["id"],
                    CoverUri = pljs["picUrl"].ToString(),
                    Order = i++,
                    CanPlay = true
                });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadArtistResult()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistSublist,
                new Dictionary<string, object>
                {
                    { "offset", page * 25 }
                });

            BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var singerjson in json["data"])
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = singerjson["name"].ToString(),
                    LineOne = singerjson["trans"].ToString(),
                    LineTwo = string.Join(" / ",
                        singerjson["alia"]?.Select(t => t.ToString()) ?? new List<string>()),
                    LineThree = $"专辑数 {singerjson["albumSize"]} | MV 数 {singerjson["mvSize"]}",
                    ResourceId = "ar" + singerjson["id"],
                    CoverUri = singerjson["img1v1Url"].ToString(),
                    Order = i++,
                    CanPlay = true
                });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadAlbumResult()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.AlbumSublist,
                new Dictionary<string, object>
                {
                    { "offset", page * 25 }
                });
            BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var albumjson in json["data"])
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = albumjson["name"].ToString(),
                    LineOne = string.Join(" / ", albumjson["artists"].Select(t => t["name"].ToString())),
                    LineTwo = albumjson["alias"] != null
                        ? string.Join(" / ", albumjson["alias"].ToArray().Select(t => t.ToString()))
                        : "",
                    LineThree = albumjson.Value<bool>("paid") ? "付费专辑" : "",
                    ResourceId = "al" + albumjson["id"],
                    CoverUri = albumjson["picUrl"].ToString(),
                    Order = i++,
                    CanPlay = true
                });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void BtnLoadMore_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        page++;
        RealLoad();
    }
}