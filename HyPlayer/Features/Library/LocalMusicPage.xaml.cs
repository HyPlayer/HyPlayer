#region

using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed partial class LocalMusicPage : Page
{
    private static readonly string[] _supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };
    private readonly CancellationToken _cancellationToken;
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private readonly ILocalFileImportService _localFileImport =
        Ioc.Default.GetRequiredService<ILocalFileImportService>();

    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly LocalLibrarySettings _setting = Ioc.Default.GetRequiredService<LocalLibrarySettings>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _currentFileScanTask;
    private bool _isNavigatedAway;

    public LocalMusicPageViewModel ViewModel { get; } = new();

    public LocalMusicPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isNavigatedAway = true;
        Bindings.StopTracking();
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        _cancellationTokenSource.Cancel();
        _taskRunner.Forget(CompleteScanShutdownAsync(), "stop local music scan");
    }

    private async Task CompleteScanShutdownAsync()
    {
        try
        {
            if (_currentFileScanTask is not null) await _currentFileScanTask;
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
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
        await _playCore.InsertSongRangeAsync(ViewModel.LocalItems.Cast<SingleSongBase>().ToList());
        if (ViewModel.LocalItems.Count > 0)
        {
            await _playCore.MovePointerToIndexAsync(0);
            if (_playCore.CurrentSong is { } song)
                await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_isNavigatedAway && (_currentFileScanTask == null || _currentFileScanTask.IsCompleted)) _currentFileScanTask = LoadLocalMusic();
    }

    private async Task LoadLocalMusic()
    {
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        ViewModel.NotificationText = "正在扫描...";
        ViewModel.LocalItems.Clear();
        FileLoadingIndicateRing.Visibility = Visibility.Visible;
        FileLoadingIndicateRing.IsActive = true;
        try
        {
            var folder = !string.IsNullOrEmpty(_setting.SearchDirectory)
                ? await StorageFolder.GetFolderFromPathAsync(_setting.SearchDirectory)
                : KnownFolders.MusicLibrary;
            var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, _supportedFormats)
            {
                FolderDepth = FolderDepth.Deep
            };
            var query = folder.CreateFileQueryWithOptions(queryOptions);
            var progressive = _setting.LocalProgressiveLoad;
            var album = new LocalAlbum { Name = "未知专辑 - 播放后加载", ActualId = string.Empty };
            var artists = new List<LocalArtist> { new() { Name = "未知歌手 - 播放后加载", ActualId = string.Empty } };
            var scanned = 0;
            await foreach (var files in PagedBatchReader.ReadAsync<StorageFile>(
                (start, count, token) => query.GetFilesAsync(start, count).AsTask(token), 64, _cancellationToken))
            {
                var items = new List<LocalSong>(files.Count);
                if (progressive)
                {
                    items = await Task.Run(() =>
                    {
                        var batch = new List<LocalSong>(files.Count);
                        foreach (var file in files)
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            batch.Add(new LocalSong
                            {
                                Album = album, Artists = artists,
                                CreatorList = artists.Select(artist => artist.Name ?? string.Empty).ToList(),
                                StorageFile = file, Name = file.Name, CdName = "01",
                                ExtensionName = file.FileType, InfoTag = "本地歌曲",
                                ActualId = file.Path, Available = true
                            });
                        }
                        return batch;
                    }, _cancellationToken);
                }
                else
                {
                    foreach (var file in files)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        try { items.Add(await _localFileImport.LoadStorageFileAsync(file)); }
                        catch (OperationCanceledException) { throw; }
                        catch { /* Skip unreadable audio. */ }
                    }
                }
                _cancellationToken.ThrowIfCancellationRequested();
                for (var i = 0; i < items.Count; i++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    ViewModel.LocalItems.Add(items[i]);
                    if ((i + 1) % 32 == 0) await Task.Yield();
                }
                _cancellationToken.ThrowIfCancellationRequested();
                scanned += files.Count;
                ViewModel.NotificationText = "正在扫描...已检查 " + scanned + " 个文件";
            }
            _cancellationToken.ThrowIfCancellationRequested();
            ViewModel.NotificationText = "扫描完成, 共 " + ViewModel.LocalItems.Count + " 首音乐";
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_isNavigatedAway) ViewModel.NotificationText = "扫描失败: " + ex.Message;
        }
        finally
        {
            if (!_isNavigatedAway)
            {
                FileLoadingIndicateRing.IsActive = false;
                FileLoadingIndicateRing.Visibility = Visibility.Collapsed;
                ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
            }
        }
    }

    private async void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxLocalMusicContainer.SelectedItem == null) return;
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(ViewModel.LocalItems.Cast<SingleSongBase>().ToList());
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

}
