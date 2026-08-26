using System.Threading.Tasks;
using Windows.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Storage.Cache;
using System;

namespace HyPlayer.Features.Settings;

public partial class SettingsViewModel(
    IHistoryService history,
    IPlaybackMemoryService playbackMemory,
    PlaybackSettings playback,
    UISettings ui,
    ApiSettings api,
    LyricSettings lyric,
    LastFMSettings lastFM,
    DownloadSettings download,
    LocalLibrarySettings localLibrary) : ObservableObject
{
    public PlaybackSettings Playback { get; } = playback;
    public UISettings UI { get; } = ui;
    public ApiSettings Api { get; } = api;
    public LyricSettings Lyric { get; } = lyric;
    public LastFMSettings LastFM { get; } = lastFM;
    public DownloadSettings Download { get; } = download;
    public LocalLibrarySettings LocalLibrary { get; } = localLibrary;

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        await history.ClearHistoryAsync();
        await playbackMemory.ClearAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await SimpleCacher.ClearAllCacheAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Playback.CacheDirectory);
        foreach (var file in await folder.GetFilesAsync())
        {
            if (file.FileType is ".flac" or ".mp3")
                await file.DeleteAsync();
        }
    }
}
