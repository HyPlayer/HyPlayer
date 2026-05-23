using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.LocalProvider;

public sealed class LocalProvider : ProviderBase, IMusicResourceProvidable
{
    public override string Name => "本地音乐";
    public override string Id => "lcl";

    public override List<ProvidableTypeId> ProvidableTypeIds =>
        new()
        {
            new("sg", "本地歌曲", true),
            new("ncm", "NCM 歌曲", true),
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

            // TODO: Add NCM decryption support for local encrypted files.
            MusicResourceBase resource = new LocalMusicResource
            {
                Uri = new Uri(path),
                ResourceName = song.Name,
                HasContent = true,
                ExtensionName = Path.GetExtension(path),
            };

            return Task.FromResult<MusicResourceBase?>(resource);
        }
        catch (Exception)
        {
            return Task.FromResult<MusicResourceBase?>(null);
        }
    }

    private sealed class LocalMusicResource : MusicResourceBase
    {
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
    }

    private sealed class LocalMusicResourceResult : ResourceResultBase
    {
        public override required Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
    }
}
