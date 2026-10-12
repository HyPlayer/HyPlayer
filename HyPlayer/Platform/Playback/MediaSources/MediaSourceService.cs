using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class MediaSourceService : IMediaSourceService
{
    private readonly Dictionary<string, IMediaSourceProvider> _providers;
    private readonly NeteaseSongUrlResolver _urlResolver;

    public MediaSourceService(IEnumerable<IMediaSourceProvider> providers, NeteaseSongUrlResolver urlResolver)
    {
        _providers = providers?.ToDictionary(provider => provider.Id, StringComparer.Ordinal)
                     ?? throw new ArgumentNullException(nameof(providers));
        _urlResolver = urlResolver ?? throw new ArgumentNullException(nameof(urlResolver));
    }

    public async Task<MusicResourceBase?> ResolveAsync(SingleSongBase song, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ResolvedSongUrl? resolved;
        try
        {
            resolved = await _urlResolver.ResolveAsync(song.ActualId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return resolved is null ? null : CreateResource(song, resolved);
    }

    public Task<MediaSource?> CreateMediaSourceAsync(
        SingleSongBase song,
        MusicResourceBase resource,
        CancellationToken ct)
    {
        var providerId = SelectProviderId();
        if (!_providers.TryGetValue(providerId, out var provider))
            throw new NotSupportedException($"未找到 ProviderId='{providerId}' 对应的 IMediaSourceProvider");

        return provider.CreateAsync(song, resource, ct);
    }

    private static string SelectProviderId()
    {
        return NeteaseStreamingProvider.ProviderId;
    }

    private NeteaseMediaResource CreateResource(SingleSongBase song, ResolvedSongUrl resolved)
    {
        var songUrl = resolved.Item;
        return new NeteaseMediaResource(song, this)
        {
            Md5 = songUrl.Md5,
            Size = songUrl.Size,
            BitRate = songUrl.BitRate,
            EncodeType = songUrl.EncodeType,
            Time = songUrl.Time,
            MusicType = songUrl.Type,
            ExtensionName = string.IsNullOrWhiteSpace(songUrl.Type) ? songUrl.EncodeType : songUrl.Type,
            Level = songUrl.Level,
            GainDb = songUrl.Gain,
            Peak = songUrl.Peak,
            ResourceName = song.Name,
            HasContent = true,
            Uri = resolved.Uri
        };
    }
}
