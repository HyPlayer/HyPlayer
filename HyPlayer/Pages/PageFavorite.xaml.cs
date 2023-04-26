#region

using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

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
    private Task _listLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public PageFavorite()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        ItemContainer.ListItems.Clear();
        _cancellationTokenSource.Dispose();
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_listLoaderTask != null && !_listLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _listLoaderTask;
            }
            catch
            {
                Dispose();
                return;
            }
        }
        Dispose();
    }

    private void NavView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        page = 0;
        i = 0;
        ItemContainer.ListItems.Clear();
        _listLoaderTask =  RealLoad();
    }

    private async Task RealLoad()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(PageFavorite));
        _cancellationToken.ThrowIfCancellationRequested();
        switch ((NavView.SelectedItem as NavigationViewItem)?.Tag.ToString())
        {
            case "Album":
                await LoadAlbumResult();
                break;
            case "Artist":
                await LoadArtistResult();
                break;
            case "Radio":
                await LoadRadioResult();
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
            {
                _cancellationToken.ThrowIfCancellationRequested();
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
            {
                _cancellationToken.ThrowIfCancellationRequested();
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
            {
                _cancellationToken.ThrowIfCancellationRequested();
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
        _listLoaderTask = RealLoad();
    }
}