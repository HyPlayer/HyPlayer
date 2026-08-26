using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Storage;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;

namespace HyPlayer.Features.Library;

public partial class LocalMusicPageViewModel : ObservableObject
{
    private static readonly string[] SupportedFormats = [".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav"];
    private readonly IPlaybackControlService _control;
    private readonly ILocalFileImportService _localFileImport;
    private readonly PlayCoreBase _playCore;
    private readonly LocalLibrarySettings _settings;
    private Task? _scanTask;

    public LocalMusicPageViewModel(
        IPlaybackControlService control,
        ILocalFileImportService localFileImport,
        PlayCoreBase playCore,
        LocalLibrarySettings settings)
    {
        _control = control;
        _localFileImport = localFileImport;
        _playCore = playCore;
        _settings = settings;
        LocalItemsView = LocalItems.ToNotifyCollectionChanged();
    }

    [ObservableProperty] public partial bool IsScanning { get; set; }
    [ObservableProperty] public partial string NotificationText { get; set; } = string.Empty;

    public ObservableList<LocalSong> LocalItems { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<LocalSong> LocalItemsView { get; }

    public Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_scanTask is { IsCompleted: false })
            return _scanTask;
        _scanTask = ScanCoreAsync(cancellationToken);
        return _scanTask;
    }

    [RelayCommand]
    private async Task PlayAllAsync()
    {
        await ReplaceQueueAsync(LocalItems.Count > 0 ? LocalItems[0] : null);
    }

    [RelayCommand]
    private async Task PlayAsync(LocalSong song)
    {
        await ReplaceQueueAsync(song);
    }

    [RelayCommand]
    private async Task AppendFilesAsync()
    {
        var items = await _localFileImport.PickLocalFilesAsync();
        if (items.Count == 0)
            return;

        await _playCore.InsertSongRangeAsync(items.Cast<SingleSongBase>().ToList());
        await _playCore.MovePointerToAsync(items[^1]);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }

    private async Task ScanCoreAsync(CancellationToken cancellationToken)
    {
        IsScanning = true;
        NotificationText = "正在扫描...";
        LocalItems.Clear();
        try
        {
            var folder = !string.IsNullOrEmpty(_settings.SearchDirectory)
                ? await StorageFolder.GetFolderFromPathAsync(_settings.SearchDirectory)
                : KnownFolders.MusicLibrary;
            var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, SupportedFormats)
            {
                FolderDepth = FolderDepth.Deep
            };
            var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();
            var localItems = new List<LocalSong>(files.Count);

            if (!_settings.LocalProgressiveLoad)
            {
                foreach (var storageFile in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        localItems.Add(await _localFileImport.LoadStorageFileAsync(storageFile));
                    }
                    catch
                    {
                        // A malformed local file must not abort the rest of the scan.
                    }
                }
            }
            else
            {
                var undeterminedAlbum = new LocalAlbum { Name = "未知专辑 - 播放后加载", ActualId = string.Empty };
                var undeterminedArtists = new List<LocalArtist>
                {
                    new() { Name = "未知歌手 - 播放后加载", ActualId = string.Empty }
                };
                foreach (var storageFile in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    localItems.Add(new LocalSong
                    {
                        Album = undeterminedAlbum,
                        Artists = undeterminedArtists,
                        CreatorList = undeterminedArtists.Select(artist => artist.Name ?? string.Empty).ToList(),
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
                    });
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            LocalItems.AddRange(localItems);
            NotificationText = $"扫描完成, 共 {files.Count} 首音乐";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ReplaceQueueAsync(LocalSong? selectedSong)
    {
        await _control.StopAsync();
        await _control.ClearQueueAsync();
        await _playCore.InsertSongRangeAsync(LocalItems.Cast<SingleSongBase>().ToList());
        if (selectedSong is null)
            return;

        await _playCore.MovePointerToAsync(selectedSong);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }
}
