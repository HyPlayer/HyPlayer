#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Library;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class LocalMusicPage : Page, INotifyPropertyChanged
{
    private static readonly string[] supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };
    private readonly CancellationToken _cancellationToken;
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private readonly ILocalFileImportService _localFileImport =
        Ioc.Default.GetRequiredService<ILocalFileImportService>();

    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly ObservableCollection<LocalSong> localHyItems = new();
    private string _notificationText;
    private Task CurrentFileScanTask;

    public LocalMusicPage()
    {
        InitializeComponent();
        _cancellationToken = cancellationTokenSource.Token;
    }

    public string NotificationText
    {
        get => _notificationText;
        set
        {
            _notificationText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (CurrentFileScanTask != null && !CurrentFileScanTask.IsCompleted)
            try
            {
                NotificationText = "正在等待本地扫描进程结束...";
                cancellationTokenSource.Cancel();
                await CurrentFileScanTask;
            }
            catch
            {
                CurrentFileScanTask = null;
            }

        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        cancellationTokenSource.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadPageFrame.Navigate(typeof(DownloadPage));
    }

    private async void Playall_Click(object sender, RoutedEventArgs e)
    {
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(localHyItems.Cast<SingleSongBase>().ToList());
        if (localHyItems.Count > 0)
        {
            await _playCore.MovePointerToIndexAsync(0);
            if (_playCore.CurrentSong is { } song)
                await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFileScanTask == null || CurrentFileScanTask.IsCompleted) CurrentFileScanTask = LoadLocalMusic();
    }

    private async Task LoadLocalMusic()
    {
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        NotificationText = "正在扫描...";
        localHyItems.Clear();
        var folder = !string.IsNullOrEmpty(_setting.searchingDir)
            ? await StorageFolder.GetFolderFromPathAsync(_setting.searchingDir)
            : KnownFolders.MusicLibrary;
        // Use Query to boost? maybe?
        FileLoadingIndicateRing.Visibility = Visibility.Visible;
        FileLoadingIndicateRing.IsActive = true;
        var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, supportedFormats);
        queryOptions.FolderDepth = FolderDepth.Deep;
        var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

        if (!_setting.localProgressiveLoad)
        {
            foreach (var storageFile in files)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var item = await _localFileImport.LoadStorageFileAsync(storageFile);
                    localHyItems.Add(item);
                }
                catch
                {
                    //ignore
                }
            }
        }
        else
        {
            var undeterminedAlbum = new LocalAlbum { Name = "未知专辑 - 播放后加载", ActualId = string.Empty };
            var undeterminedArtistList = new List<LocalArtist>
                { new() { Name = "未知歌手 - 播放后加载", ActualId = string.Empty } };
            foreach (var storageFile in files)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var item = new LocalSong
                {
                    Album = undeterminedAlbum,
                    Artists = undeterminedArtistList,
                    CreatorList = undeterminedArtistList.Select(artist => artist.Name ?? string.Empty).ToList(),
                    Bitrate = 0,
                    StorageFile = storageFile,
                    Duration = 0,
                    Name = storageFile.Name,
                    CdName = "01",
                    ExtensionName = storageFile.FileType,
                    TrackNumber = 0,
                    InfoTag = "本地歌曲",
                    ActualId = storageFile.Path,
                    Available = true
                };
                localHyItems.Add(item);
            }
        }

        NotificationText = "扫描完成, 共 " + files.Count + " 首音乐";
        FileLoadingIndicateRing.IsActive = false;
        FileLoadingIndicateRing.Visibility = Visibility.Collapsed;
        ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
    }


    private async void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxLocalMusicContainer.SelectedItem == null) return;
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(localHyItems.Cast<SingleSongBase>().ToList());
        if (ListBoxLocalMusicContainer.SelectedItem is LocalSong selectedItem)
        {
            await _playCore.MovePointerToAsync(selectedItem);
            if (_playCore.CurrentSong is { } song)
                await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
        }
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        var sf = await StorageFile.GetFileFromPathAsync((sender?.As<Button>()).Tag as string);
        await CloudUpload.UploadMusic(sf);
    }

    private void Add_Local(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(PickAndAppendLocalFilesAsync, "pick local music from local music page");
    }

    private async Task PickAndAppendLocalFilesAsync()
    {
        var items = await _localFileImport.PickLocalFilesAsync();
        if (items.Count == 0)
            return;

        await _playCore.InsertSongRangeAsync(items.Cast<SingleSongBase>().ToList());
        await _playCore.MovePointerToAsync(items[items.Count - 1]);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}