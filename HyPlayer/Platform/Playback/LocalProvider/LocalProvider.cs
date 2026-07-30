using HyPlayer.Domain.Music;
using HyPlayer.Platform.Network;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Platform.Playback.LocalProvider;

public sealed class LocalProvider : ProviderBase, IMusicResourceProvidable
{
    public const string ProviderIdValue = "lcl";
    public const string LocalSongTypeId = "sg";
    public const string LocalNcmSongTypeId = "ncm";

    public override string Name => "本地音乐";
    public override string Id => ProviderIdValue;

    public override List<ProvidableTypeId> ProvidableTypeIds =>
        new()
        {
            new(LocalSongTypeId, "本地歌曲", true),
            new(LocalNcmSongTypeId, "NCM 歌曲", true),
        };

    public Task<MusicResourceBase?> GetMusicResourceAsync(
        SingleSongBase song,
        ResourceQualityTag? qualityTag = null,
        CancellationToken ctk = default)
    {
        if (string.IsNullOrWhiteSpace(song.ActualId))
            return Task.FromResult<MusicResourceBase?>(null);

        try
        {
            var path = song.ActualId;
            if (!Path.IsPathFullyQualified(path))
                return Task.FromResult<MusicResourceBase?>(null);

            MusicResourceBase resource = new LocalMusicResource
            {
                Uri = new Uri(path),
                ResourceName = song.Name,
                HasContent = true,
                ExtensionName = Path.GetExtension(path),
                TypeId = song.TypeId,
            };

            return Task.FromResult<MusicResourceBase?>(resource);
        }
        catch (Exception)
        {
            return Task.FromResult<MusicResourceBase?>(null);
        }
    }

    private sealed class LocalMusicResource : MusicResourceBase, IChopinPlaybackSourceResource
    {
        public required string TypeId { get; init; }

        public double? SuggestedVolume => 1d;

        public override Task<ResourceResultBase> GetResourceAsync(
            ResourceQualityTag? qualityTag = null,
            CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();

            var exists = Uri?.IsFile == true && File.Exists(Uri.LocalPath);
            return Task.FromResult<ResourceResultBase>(new LocalMusicResourceResult
            {
                ExternalException = exists ? null : new FileNotFoundException("Local music file was not found.", Uri?.LocalPath),
                ResourceStatus = exists ? ResourceStatus.Success : ResourceStatus.Fail,
            });
        }

        public async Task<AudioGraphPlaybackSource?> CreatePlaybackSourceAsync(CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();
            if (Uri?.IsFile != true)
                return null;

            var file = await StorageFile.GetFileFromPathAsync(Uri.LocalPath);
            MediaSource mediaSource;
            if (TypeId == "ncm")
            {
                using var stream = await file.OpenStreamForReadAsync();
                if (!NCMFile.IsCorrectNCMFile(stream))
                    return null;

                var info = NCMFile.GetNCMMusicInfo(stream);
                var playableStream = new InMemoryRandomAccessStream();
                await NCMFile.CopyDecryptedContentToAsync(stream, playableStream.AsStreamForWrite(), ctk);
                playableStream.Seek(0);
                mediaSource = MediaSource.CreateFromStream(playableStream, MIMEHelper.GetNCMFileMimeType(info.Format));
            }
            else
            {
                mediaSource = MediaSource.CreateFromStorageFile(file);
            }

            return new AudioGraphPlaybackSource(mediaSource);
        }
    }

    private sealed class LocalMusicResourceResult : ResourceResultBase
    {
        public override required Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
    }
}
