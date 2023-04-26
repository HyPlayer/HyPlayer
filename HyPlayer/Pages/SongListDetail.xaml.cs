#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class SongListDetail : Page, IDisposable
{
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        "IsLoading", typeof(bool), typeof(SongListDetail), new PropertyMetadata(true));

    private bool isDescExpanded = false;
    private int page;
    private NCPlayList playList;
    public ObservableCollection<NCSong> Songs;
    public bool IsDisposed = false;
    private DataTransferManager _dataTransferManager = DataTransferManager.GetForCurrentView();
    private Task _songListLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public SongListDetail()
    {
        InitializeComponent();
        Songs = new ObservableCollection<NCSong>();
        _dataTransferManager.DataRequested += DataTransferManagerOnDataRequested;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    private void DataTransferManagerOnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dp = new DataPackage();
        dp.Properties.Title = playList.name;
        dp.SetWebLink(new Uri("https://music.163.com/#/playlist?id=" +
                              playList.plid));
        var request = args.Request;
        request.Data = dp;
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        Songs.Clear();
        SongsList.Dispose();
        playList = null;
        IsDisposed = true;
        ImageRect.ImageSource = null;
        _dataTransferManager.DataRequested -= DataTransferManagerOnDataRequested;
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }

    public void LoadSongListDetail()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        if (playList.cover.StartsWith("http"))
            ImageRect.ImageSource =
                Common.Setting.noImage
                    ? null
                    : new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
        else
            ImageRect.ImageSource =
                Common.Setting.noImage ? null : new BitmapImage(new Uri(playList.cover));


        TextBoxPLName.Text = playList.name;
        DescriptionWrapper.Text = playList.desc;
        TextBoxAuthor.Content = playList.creater.name;
        ToggleButtonLike.IsChecked = playList.subscribed;
        if (playList.trackCount != 0)
            TextBoxPlayCount.Text = $"{playList.trackCount}首歌曲";

        if (playList.updateTime.Year != 0001)
            TextBoxUpdateTime.Text = $"{DateConverter.FriendFormat(playList.updateTime)}更新";
    }

    public async Task LoadSongListItem()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        IsLoading = true;
        if (playList.plid != "-666")
        {
            await LoadPlayListItems();
        }
        else
        {
            ButtonIntel.Visibility = Visibility.Collapsed;
            await LoadDailyRcmdItems();
        }

        IsLoading = false;
    }

    private async Task LoadDailyRcmdItems()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        SongsList.ListSource = "content";
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.RecommendSongs);
            if (json["data"]["dailySongs"][0]["alg"].ToString() == "birthDaySong")
            {
                // 诶呀,没想到还过生了,吼吼
                DescriptionWrapper.Text = "生日快乐~ 今天也要开心哦!";
                DescriptionWrapper.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
                DescriptionWrapper.FontSize = 25;
            }

            var idx = 0;
            foreach (var song in json["data"]["dailySongs"])
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    throw new TaskCanceledException();
                }
                var ncSong = NCSong.CreateFromJson(song);
                ncSong.IsAvailable = true;
                ncSong.Order = idx++;
                Songs.Add(ncSong);
            }
        }
        catch (Exception ex)
        {
            if(ex.GetType()!=typeof(TaskCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadPlayListItems()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                new Dictionary<string, object> { { "id", playList.plid } });
            if (!json["playlist"]["trackIds"].HasValues) return;
            var trackIds = json["playlist"]["trackIds"].Select(t => (int)t["id"]).Skip(page * 500)
                .Take(500)
                .ToArray();

            NextPage.Visibility = trackIds.Length >= 500 ? Visibility.Visible : Visibility.Collapsed;


            if (json["playlist"]["specialType"].ToString() == "5" &&
                json["playlist"]["userId"].ToString() == Common.LoginedUser?.id)
            {
                ButtonIntel.Visibility = Visibility.Visible;
                SongsList.IsMySongList = true;
            }

            try
            {
                json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                    new Dictionary<string, object> { ["ids"] = string.Join(",", trackIds) });
                var idx = page * 500;
                var i = 0;
                foreach (var jToken in json["songs"])
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        throw new TaskCanceledException();
                    }
                    var song = (JObject)jToken;

                    var ncSong = NCSong.CreateFromJson(song);
                    ncSong.IsAvailable =
                        json["privileges"].ToList()[i++]["st"].ToString() == "0";
                    ncSong.Order = idx++;
                    Songs.Add(ncSong);
                }
            }

            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TaskCanceledException))
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_songListLoaderTask != null && !_songListLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _songListLoaderTask;
            }
            catch
            {
                Dispose();
                return;
            }
        }
        Dispose();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter != null)
        {
            if (e.Parameter is NCPlayList)
            {
                playList = (NCPlayList)e.Parameter;
            }
            else
            {
                var pid = e.Parameter.ToString();

                try
                {
                    var json = await Common.ncapi.RequestAsync(
                        CloudMusicApiProviders.PlaylistDetail,
                        new Dictionary<string, object> { { "id", pid } });
                    if (json["code"].ToString() != "200")
                        throw new Exception(json["message"]?.ToString());
                    playList = NCPlayList.CreateFromJson(json["playlist"]);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
                }
            }
        }

        SongsList.ListSource = "pl" + playList?.plid;
        LoadSongListDetail();
        _songListLoaderTask = LoadSongListItem();
    }


    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        if (playList.plid != "-666")
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.SongAppendDone();
            await HyPlayList.AppendPlayList(playList.plid);
            HyPlayList.SongAppendDone();
            HyPlayList.PlaySourceId = playList.plid;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }
        else
        {
            HyPlayList.AppendNcSongs(Songs.ToList());
            HyPlayList.SongAppendDone();
            HyPlayList.PlaySourceId = playList.plid;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }
    }


    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        page++;
        _songListLoaderTask = LoadSongListItem();
    }

    private void ButtonComment_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        Common.NavigatePage(typeof(Comments), "pl" + playList.plid);
    }

    private async void ButtonHeartBeat_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        HyPlayList.RemoveAllSong();
        try
        {
            var jsona = await Common.ncapi.RequestAsync(
                CloudMusicApiProviders.PlaymodeIntelligenceList,
                new Dictionary<string, object>
                    { { "pid", playList.plid }, { "id", Songs[0].sid } /*, { "sid", Songs[0].sid }*/ });
            var IntSongs = new List<NCSong>();
            IntSongs.Add(Songs[new Random().Next(0, Songs.Count)]);
            foreach (var token in jsona["data"])
                try
                {
                    if (token["songInfo"] != null)
                    {
                        var ncSong = NCSong.CreateFromJson(token["songInfo"]);
                        IntSongs.Add(ncSong);
                    }
                }
                catch
                {
                    //ignore
                }

            try
            {
                HyPlayList.AppendNcSongs(IntSongs);
                HyPlayList.SongAppendDone();
                HyPlayList.PlaySourceId = playList.plid;
                HyPlayList.SongMoveTo(0);
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

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        DownloadManager.AddDownload(Songs.ToList());
    }

    private void LikeBtnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistSubscribe,
            new Dictionary<string, object> { { "id", playList.plid }, { "t", playList.subscribed ? "0" : "1" } });
        playList.subscribed = !playList.subscribed;
    }

    private void TextBoxAuthor_Tapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        Common.NavigatePage(typeof(Me), playList.creater.id);
    }

    private void BtnShare_Clicked(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail)); 
        DataTransferManager.ShowShareUI();
    }
    

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(SongListDetail));
        if (playList.plid != "-666")
            await HyPlayList.AppendPlayList(playList.plid);
        else
            HyPlayList.AppendNcSongs(Songs.ToList());
        HyPlayList.SongAppendDone();
    }
}