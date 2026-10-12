using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Downloads;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class MediaSourceService : IMediaSourceService
{
    private readonly PlaybackSettings _playbackSettings;
    private readonly Dictionary<string, IMediaSourceProvider> _providers;
    private readonly NeteaseSongUrlResolver _urlResolver;

    public MediaSourceService(
        IEnumerable<IMediaSourceProvider> providers,
        NeteaseSongUrlResolver urlResolver,
        PlaybackSettings playbackSettings)
    {
        _providers = providers?.ToDictionary(provider => provider.Id, StringComparer.Ordinal)
                     ?? throw new ArgumentNullException(nameof(providers));
        _urlResolver = urlResolver ?? throw new ArgumentNullException(nameof(urlResolver));
        _playbackSettings = playbackSettings ?? throw new ArgumentNullException(nameof(playbackSettings));
    }

    public async Task PrefetchAsync(SingleSongBase song, CancellationToken ct)
    {
        if (!IsNeteaseSong(song)
            || !_providers.TryGetValue(SelectProviderId(), out var provider)
            || provider is not IMediaPrefetchProvider prefetcher
            || await DownloadedTrackIndex.TryGetAsync(song.ActualId).ConfigureAwait(false) is not null)
            return;

        var resolved = await _urlResolver.ResolveAsync(song.ActualId, ct).ConfigureAwait(false);
        if (resolved is null)
            return;

        await prefetcher.PrefetchAsync(song, CreateResource(song, resolved), ct).ConfigureAwait(false);
    }

    public bool IsTransferring(SingleSongBase song)
    {
        foreach (var provider in _providers.Values)
            if (provider is IMediaTransferProvider transfers && transfers.IsTransferring(song))
                return true;

        return false;
    }

    public void CancelTransfersExcept(SingleSongBase keep)
    {
        foreach (var provider in _providers.Values)
            if (provider is IMediaTransferProvider transfers)
                transfers.CancelTransfersExcept(keep);
    }

    public async Task<bool> CanSeekBeforePlaybackAsync(SingleSongBase song, CancellationToken ct)
    {
        if (!IsNeteaseSong(song)
            || !_providers.TryGetValue(SelectProviderId(), out var provider)
            || provider is not IMediaTransferProvider transfers)
            return true;

        if (await DownloadedTrackIndex.TryGetAsync(song.ActualId).ConfigureAwait(false) is not null)
            return true;

        if (transfers.IsTransferring(song))
            return false;

        ResolvedSongUrl? resolved;
        try
        {
            resolved = await _urlResolver.ResolveAsync(song.ActualId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return true;
        }

        return resolved is null
               || await transfers.IsCachedAsync(song, CreateResource(song, resolved), ct).ConfigureAwait(false);
    }

    public async Task<MusicResourceBase?> ResolveAsync(SingleSongBase song, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsNeteaseSong(song)
            && await DownloadedTrackIndex.TryGetAsync(song.ActualId).ConfigureAwait(false) is { } downloaded)
            return new LocalFileMediaResource(song, downloaded);

        ResolvedSongUrl? resolved;
        try
        {
            resolved = await _urlResolver.ResolveAsync(song.ActualId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_providers.TryGetValue(SelectProviderId(), out var provider)
                && provider is IMediaOfflineProvider offline
                && await offline.FindOfflineFileAsync(song).ConfigureAwait(false) is { } offlineFile)
                return new LocalFileMediaResource(song, offlineFile);

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

    private string SelectProviderId()
    {
        return _playbackSettings.EnableCache
            ? CachedNeteaseProvider.ProviderId
            : NeteaseStreamingProvider.ProviderId;
    }

    private static bool IsNeteaseSong(SingleSongBase song)
    {
        return song.ProviderId == "ncm" && !string.IsNullOrWhiteSpace(song.ActualId);
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
