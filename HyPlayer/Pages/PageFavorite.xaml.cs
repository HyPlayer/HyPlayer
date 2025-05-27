#region

using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using System;
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
    private bool disposedValue = false;
    private Task _listLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public PageFavorite()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
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
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
        page = 0;
        i = 0;
        ItemContainer.ListItems.Clear();
        _listLoaderTask = RealLoad();
    }

    private async Task RealLoad()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
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
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.DjChannelSubscribedApi, _cancellationToken);
            if (json.IsError)
            {
                Common.AddToTeachingTipLists("加载订阅播客列表错误", json.Error.Message);
                return;
            }

            BtnLoadMore.Visibility = json.Value?.Data?.HasMore is true ? Visibility.Visible : Visibility.Collapsed;
            foreach (var pljs in json.Value?.Data?.Data ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = pljs.Name,
                    LineOne = pljs.UserName,
                    LineTwo = pljs.Description,
                    LineThree =
                        $"{DateConverter.FriendFormat(DateConverter.GetDateTimeFromTimeStamp(pljs.LastProgramCreateTime))}前 | 最后一个节目: " +
                        pljs.LastVoiceName,
                    ResourceId = "rd" + pljs.Id,
                    CoverLink = pljs.CoverUrl,
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
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.ArtistSublistApi,
                new ArtistSublistRequest()
                {
                    Limit = 25,
                    Offset = page * 25
                });

            if (json.IsError)
            {
                Common.AddToTeachingTipLists("加载关注歌手列表错误", json.Error.Message);
                return;
            }

            BtnLoadMore.Visibility = json.Value.HasMore ? Visibility.Visible : Visibility.Collapsed;
            foreach (var singerjson in json.Value.Artists ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = singerjson.Name,
                    LineOne = singerjson.Translation,
                    LineTwo = string.Join("/", singerjson.Alias ?? []),
                    LineThree = $"专辑数 {singerjson.AlbumSize} | MV 数 {singerjson.MvSize}",
                    ResourceId = "ar" + singerjson.Id,
                    CoverLink = singerjson.Img1v1Url,
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
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
        try
        {
            var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.AlbumSublistApi,
                new AlbumSublistRequest()
                {
                    Limit = 25,
                    Offset = page * 25
                });
            if (json.IsError)
            {
                Common.AddToTeachingTipLists("加载关注专辑列表错误", json.Error.Message);
                return;
            }
            BtnLoadMore.Visibility = json.Value?.HasMore is true ? Visibility.Visible : Visibility.Collapsed;
            foreach (var albumjson in json.Value?.Data ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                ItemContainer.ListItems.Add(new SimpleListItem
                {
                    Title = albumjson.Name,
                    LineOne = string.Join(" / ", albumjson.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = string.Join(" / ", albumjson.Alias ?? []),
                    LineThree = $"歌曲数:{albumjson.Size}",
                    ResourceId = "al" + albumjson.Id,
                    CoverLink = albumjson.PictureUrl,
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
        if (disposedValue) throw new ObjectDisposedException(nameof(PageFavorite));
        page++;
        _listLoaderTask = RealLoad();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                ItemContainer.ListItems.Clear();
                _cancellationTokenSource.Dispose();
            }

            disposedValue = true;
        }
    }

    ~PageFavorite()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}