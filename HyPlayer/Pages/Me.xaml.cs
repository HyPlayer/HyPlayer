#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Me : Page, IDisposable
{
    private readonly ObservableCollection<SimpleListItem> likedPlayList = new();
    private readonly ObservableCollection<SimpleListItem> myPlayList = new();
    private string uid = "";
    private bool disposedValue = false;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadPlaylistTask;
    private Task _loadInfoTask;

    public Me()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if ((_loadPlaylistTask != null && !_loadPlaylistTask.IsCompleted)
            || (_loadInfoTask != null && !_loadInfoTask.IsCompleted))
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadPlaylistTask;
                await _loadInfoTask;
            }
            catch
            {
                Dispose();
                return;
            }
        }
        Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter != null)
        {
            uid = (string)e.Parameter;
            ButtonLogout.Visibility = Visibility.Collapsed;
        }
        else
        {
            uid = Common.LoginedUser.id;
        }

        _loadInfoTask = LoadInfo();
        _loadPlaylistTask = LoadPlayList();
    }

    public async Task LoadPlayList()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Me));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                new Dictionary<string, object> { ["uid"] = uid, ["limit"] = 999 });


            var myListIdx = 0;
            var subListIdx = 0;
            foreach (var PlaylistItemJson in json["playlist"].ToArray())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                if (ncp.creater.id != uid)
                    //GridContainerSub.Children.Add(new PlaylistItem(ncp));
                    likedPlayList.Add(
                        new SimpleListItem
                        {
                            CoverLink = ncp.cover.ToString(),
                            LineOne = ncp.creater.name,
                            LineThree = null,
                            LineTwo = null,
                            Order = myListIdx++,
                            ResourceId = "pl" + ncp.plid,
                            Title = ncp.name,
                            CanPlay = true
                        }
                    );
                else
                    myPlayList.Add(
                        new SimpleListItem
                        {
                            CoverLink = ncp.cover.ToString(),
                            LineOne = ncp.creater.name,
                            LineThree = null,
                            LineTwo = null,
                            Order = subListIdx++,
                            ResourceId = "pl" + ncp.plid,
                            Title = ncp.name,
                            CanPlay = true
                        }
                    );
            }
            json.RemoveAll();
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    public async Task LoadInfo()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Me));
        _cancellationToken.ThrowIfCancellationRequested();
        if (uid == Common.LoginedUser?.id)
        {
            TextBoxUserName.Text = Common.LoginedUser.name;
            TextBoxSignature.Text = Common.LoginedUser.signature;
            ImageRect.ImageSource = Common.Setting.noImage
                ? null
                : new BitmapImage(new Uri(Common.LoginedUser.avatar, UriKind.RelativeOrAbsolute));
        }
        else
        {
            try
            {
                var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.UserDetail,
                    new Dictionary<string, object> { ["uid"] = uid });

                TextBoxUserName.Text = json["profile"]["nickname"].ToString();
                TextBoxSignature.Text = json["profile"]["signature"].ToString();
                if (Common.Setting.noImage)
                {
                    ImageRect.ImageSource = null;
                }
                else
                {
                    var img = new BitmapImage();
                    ImageRect.ImageSource = img;
                    img.UriSource = new Uri(json["profile"]["avatarUrl"].ToString());
                }
                json.RemoveAll();
            }
            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        /*
        (bool isok, JObject json) = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.UserLevel);
        if (isok)
        {
            TextBlockLevel.Text = "LV. " + json["data"]["level"].ToString();
        }
        */
    }

    private async void Logout_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Me));
        try
        {
            await Common.ncapi?.RequestAsync(CloudMusicApiProviders.Logout);
            Common.Logined = false;
            Common.LoginedUser = new NCUser();
            if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("Cookies", out var container))
            {
                container.Values.Clear();
            }
            Common.ncapi?.ClearCookies();
            Common.PageMain.MainFrame.Navigate(typeof(BasePage));
            _ = ((App)Application.Current).InitializeJumpList();
        }
        catch
        {
        }
    }

    private async void BtnPlayClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Me));
        HyPlayList.RemoveAllSong();
        await HyPlayList.AppendNcSource(((Button)sender).Tag.ToString());
        if (((Button)sender).Tag.ToString().Substring(0, 2) == "pl" ||
            ((Button)sender).Tag.ToString().Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ((Button)sender).Tag.ToString().Substring(2);

        HyPlayList.NowPlaying = -1;
        HyPlayList.SongMoveNext();
    }

    private void SongListItemClicked(object sender, TappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Me));
        _ = Common.NavigatePageResource(((Grid)sender).Tag.ToString());
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                ImageRect.ImageSource = null;
                myPlayList.Clear();
                likedPlayList.Clear();
                _cancellationTokenSource.Dispose();
            }
            disposedValue = true;
        }
    }

    ~Me()
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