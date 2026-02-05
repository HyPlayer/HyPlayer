#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Cloud;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MusicCloudPage : Page
{
    private readonly ObservableCollection<NCSong> Items = new();
    private int page;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadResultTask;

    public MusicCloudPage()
    {
        InitializeComponent();
        SongContainer.ListSource = "content";
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public async Task LoadMusicCloudItem()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "userCloud_" + page, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.UserCloudApi,
                    new UserCloudRequest()
                    {
                        Limit = 749,
                        Offset = page * 749
                    }, _cancellationToken);
                if (json is { IsError: true, Error.ErrorCode: 405 })
                {
                    treashold = ++cooldownTime * 10;
                    page--;
                    Common.AddToTeachingTipLists("贪婪加载被风控", $"渐进加载速度过于快, 将在 {cooldownTime * 10} 秒后尝试继续加载, 正在清洗请求");
                }

                return json.Value;
            });




            var idx = page * 200;
            foreach (var jToken in jv.Songs ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var ret = jToken.SongInfo.MapNcSong();
                    if (ret.Artist[0].Id == "0")
                    {
                        //不是标准歌曲
                        ret.Album.Name = jToken.AlbumName;
                        ret.Artist.Clear();
                        ret.Artist.Add(new NCArtist
                        {
                            Name = jToken.ArtistName
                        });
                    }

                    ret.IsCloud = true;
                    ret.Order = idx++;
                    SongContainer.Songs.Add(ret);
                }
                catch
                {
                    //ignore
                }

                NextPage.Visibility = jv.HasMore ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_loadResultTask != null && !_loadResultTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadResultTask;
            }
            catch
            {
                //Ignore
            }
        }

        _cancellationTokenSource.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loadResultTask = LoadMusicCloudItem();
        if (Common.Setting.greedlyLoadPlayContainerItems)
            HyPlayList.OnTimerTicked += GreedlyLoad;
    }

    int treashold = 3;
    int cooldownTime = 0;

    private void GreedlyLoad()
    {
        _ = Common.Invoke(() =>
        {
            if (treashold > 10)
            {
                treashold--;
                return;
            }

            if (SongContainer.Songs.Count > 0 && NextPage.Visibility == Visibility.Visible && treashold-- <= 0)
            {
                NextPage_OnClickPage_OnClick(null, null);
                treashold = 3;
            }
            else if (SongContainer.Songs.Count > 0 && NextPage.Visibility == Visibility.Collapsed)
            {
                HyPlayList.OnTimerTicked -= GreedlyLoad;
                OnLoadedAllSongs();
            }
        });
    }

    public void OnLoadedAllSongs()
    {
        if (Common.Setting.AutoAddGreedilyLoadedSongsToPlayList && HyPlayList.PlaySourceId == "Content")
        {
            HyPlayList.AppendNcSongRange(SongContainer.Songs.ToList());
        }
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        _loadResultTask = LoadMusicCloudItem();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        DownloadManager.AddDownload(Items.ToList());
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".flac");
        fop.FileTypeFilter.Add(".mp3");
        fop.FileTypeFilter.Add(".ncm");
        fop.FileTypeFilter.Add(".ape");
        fop.FileTypeFilter.Add(".m4a");
        fop.FileTypeFilter.Add(".wav");


        var files =
            await fop.PickMultipleFilesAsync();
        if (files == null) return;
        Common.AddToTeachingTipLists("请稍等", "正在上传 " + files.Count + " 个音乐文件");
        for (var i = 0; i < files.Count; i++)
        {
            Common.AddToTeachingTipLists("正在上传共 " + files.Count + " 个音乐文件", "正在上传 第" + i + " 个音乐文件");
            await CloudUpload.UploadMusic(files[i]);
        }

        Common.AddToTeachingTipLists("上传完成", "请重新加载云盘页面");
    }
    private async void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        await SimpleCacher.ResetCacheAsync(CacheType.Login, "userCloud_", true);
        SongContainer.Songs.Clear();
        page = 0;
        _loadResultTask = LoadMusicCloudItem();
    }
}