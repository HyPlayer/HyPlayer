using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Platform.Network;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Playback.LocalProvider;

public sealed class LocalProvider : ProviderBase, IMusicResourceProvidable
{
    public const string ProviderIdValue = "lcl";
    public const string LocalSongTypeId = "sg";
    public const string LocalNcmSongTypeId = "ncm";

    private static readonly SemaphoreSlim _ncmDecryptGate = new(1, 1);

    private static readonly ConcurrentDictionary<string, WeakReference<AudioGraphPlaybackSource>> _ncmSources =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConditionalWeakTable<AudioGraphPlaybackSource, InMemoryRandomAccessStream> _ncmStreams = new();

    internal static IRandomAccessStream? TryOpenDecryptedNcm(string path)
    {
        if (!_ncmSources.TryGetValue(path, out var reference))
            return null;

        if (reference.TryGetTarget(out var source)
            && source.PlaybackSource is not null
            && _ncmStreams.TryGetValue(source, out var stream))
            return stream.CloneStream();

        _ncmSources.TryRemove(new KeyValuePair<string, WeakReference<AudioGraphPlaybackSource>>(path, reference));
        return null;
    }

    public override string Name => "本地音乐";
    public override string Id => ProviderIdValue;

    public override List<ProvidableTypeId> ProvidableTypeIds =>
        new()
        {
            new ProvidableTypeId(LocalSongTypeId, "本地歌曲", true),
            new ProvidableTypeId(LocalNcmSongTypeId, "NCM 歌曲", true)
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
                SourcePath = path,
                Uri = new Uri(path),
                ResourceName = song.Name,
                HasContent = true,
                ExtensionName = Path.GetExtension(path),
                TypeId = song.TypeId
            };

            return Task.FromResult(resource);
        }
        catch (Exception)
        {
            return Task.FromResult<MusicResourceBase?>(null);
        }
    }

    private sealed class LocalMusicResource : MusicResourceBase, IChopinPlaybackSourceResource
    {
        public required string TypeId { get; init; }

        public required string SourcePath { get; init; }

        public double? SuggestedVolume => 1d;

        public async Task<AudioGraphPlaybackSource?> CreatePlaybackSourceAsync(CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();
            if (Uri?.IsFile != true)
                return null;

            var file = await StorageFile.GetFileFromPathAsync(Uri.LocalPath);
            MediaSource mediaSource;
            InMemoryRandomAccessStream? decrypted = null;
            if (TypeId == "ncm")
            {
                using var stream = await file.OpenStreamForReadAsync();
                if (!NCMFile.IsCorrectNCMFile(stream))
                    return null;

                var playableStream = new InMemoryRandomAccessStream();
                string mimeType;
                await _ncmDecryptGate.WaitAsync(ctk);
                try
                {
                    var info = NCMFile.GetNCMMusicInfo(stream);
                    NCMFile.GetCoverByteArray(stream);
                    var target = playableStream.AsStreamForWrite();
                    await NCMFile.CopyDecryptedContentToAsync(stream, target, ctk);
                    await target.FlushAsync(ctk);
                    mimeType = MIMEHelper.GetNCMFileMimeType(info.Format);
                }
                finally
                {
                    _ncmDecryptGate.Release();
                }

                playableStream.Seek(0);
                mediaSource = await RechunkedAudioSource.FromStreamAsync(
                    playableStream,
                    () => MediaSource.CreateFromStream(playableStream, mimeType));
                decrypted = playableStream;
            }
            else
            {
                mediaSource = string.Equals(file.FileType, ".flac", StringComparison.OrdinalIgnoreCase)
                    ? await RechunkedAudioSource.FromFileAsync(file)
                    : MediaSource.CreateFromStorageFile(file);
            }

            var source = new AudioGraphPlaybackSource(mediaSource);
            if (decrypted is not null)
            {
                _ncmStreams.Add(source, decrypted);
                _ncmSources[SourcePath] = new WeakReference<AudioGraphPlaybackSource>(source);
            }

            return source;
        }

        public override Task<ResourceResultBase> GetResourceAsync(
            ResourceQualityTag? qualityTag = null,
            CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();

            var exists = Uri?.IsFile == true && File.Exists(Uri.LocalPath);
            return Task.FromResult<ResourceResultBase>(new LocalMusicResourceResult
            {
                ExternalException =
                    exists ? null : new FileNotFoundException("Local music file was not found.", Uri?.LocalPath),
                ResourceStatus = exists ? ResourceStatus.Success : ResourceStatus.Fail
            });
        }
    }

    private sealed class LocalMusicResourceResult : ResourceResultBase
    {
        public override required Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
    }
}