using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class LocalFileMediaResource : MusicResourceBase, IChopinPlaybackSourceResource
{
    public LocalFileMediaResource(SingleSongBase song, StorageFile file)
    {
        Song = song ?? throw new ArgumentNullException(nameof(song));
        File = file ?? throw new ArgumentNullException(nameof(file));
        Uri = new Uri(file.Path);
        ResourceName = song.Name;
        HasContent = true;
        ExtensionName = file.FileType.TrimStart('.').ToLowerInvariant();
    }

    public SingleSongBase Song { get; }

    public StorageFile File { get; }

    public double? SuggestedVolume => null;

    public async Task<AudioGraphPlaybackSource?> CreatePlaybackSourceAsync(CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        return new AudioGraphPlaybackSource(await MediaSourceFiles.CreateAsync(File, ExtensionName));
    }

    public override Task<ResourceResultBase> GetResourceAsync(
        ResourceQualityTag? qualityTag = null,
        CancellationToken ctk = default)
    {
        return Task.FromResult<ResourceResultBase>(new NeteaseMusicResourceResult
        {
            ExternalException = null,
            ResourceStatus = ResourceStatus.Success
        });
    }
}

internal static class MediaSourceFiles
{
    public static async Task<MediaSource> CreateAsync(StorageFile file, string? extension)
    {
        return string.Equals(extension?.TrimStart('.'), "flac", StringComparison.OrdinalIgnoreCase)
            ? await RechunkedAudioSource.FromFileAsync(file)
            : MediaSource.CreateFromStorageFile(file);
    }
}
