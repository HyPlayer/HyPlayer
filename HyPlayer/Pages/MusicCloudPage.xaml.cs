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
public sealed partial class MusicCloudPage : Page, IDisposable
{
    private readonly ObservableCollection<NCSong> Items = new();
    private int page;
    private bool disposedValue = false;
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
        if (disposedValue) throw new ObjectDisposedException(nameof(MusicCloudPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserCloud,
                new Dictionary<string, object>
                {
                    { "limit", 200 },
                    { "offset", page * 200 }
                });
            var idx = page * 200;
            foreach (var jToken in json["data"])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var ret = NCSong.CreateFromJson(jToken["simpleSong"]);
                    if (ret.Artist[0].id == "0")
                    {
                        //不是标准歌曲
                        ret.Album.name = jToken["album"]?.ToString();
                        ret.Artist.Clear();
                        ret.Artist.Add(new NCArtist
                        {
                            name = jToken["artist"]?.ToString()
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
                NextPage.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                json.RemoveAll();
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
                Dispose();
                return;
            }
        }
        Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loadResultTask = LoadMusicCloudItem();
    }


    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MusicCloudPage));
        page++;
        _loadResultTask = LoadMusicCloudItem();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MusicCloudPage));
        DownloadManager.AddDownload(Items.ToList());
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MusicCloudPage));
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

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Items.Clear();
                SongContainer.Dispose();
                _cancellationTokenSource.Dispose();
            }
            disposedValue = true;
        }
    }

    ~MusicCloudPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}