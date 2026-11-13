using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Tiles;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
///     播放通知服务 — 负责 SMTC 显示更新、磁贴刷新、封面下载和 Last.FM Scrobble。
/// </summary>
public sealed class PlaybackNotificationService : IPlaybackNotificationService
{
    private const int CoverPixels = 1024;
    private readonly object _coverPublishGate = new();
    private readonly HttpClient _http;
    private readonly ILastFmService _lastFm;
    private readonly IPlayer _player;
    private readonly UISettings _uiSettings;
    private readonly LastFMSettings _lastFmSettings;
    private readonly PlaybackStateService _state;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly ITileService _tileService;
    private int _coverRequestVersion;
    private int _trackChangeVersion;
    private CoverPreload? _preloadedCover;

    public PlaybackNotificationService(
        PlaybackStateService state,
        UISettings uiSettings,
        LastFMSettings lastFmSettings,
        HttpClient http,
        IPlayer player,
        IBackgroundTaskRunner taskRunner,
        ITileService tileService,
        ILastFmService lastFm)
    {
        _state = state;
        _uiSettings = uiSettings;
        _lastFmSettings = lastFmSettings;
        _http = http;
        _player = player;
        _taskRunner = taskRunner;
        _tileService = tileService;
        _lastFm = lastFm;
    }

    /// <inheritdoc />
    public async Task OnTrackChangedAsync(SingleSongBase providerItem)
    {
        if (providerItem == null) return;
        var version = Interlocked.Increment(ref _trackChangeVersion);
        UpdateSmtcDisplayInfo(providerItem);

        if (!_uiSettings.NoImage)
        {
            await RefreshCoverAsync(providerItem);
            if (version != Volatile.Read(ref _trackChangeVersion)) return;
            UpdateSmtcThumbnail();
        }
        else
        {
            lock (_coverPublishGate)
            {
                Interlocked.Increment(ref _coverRequestVersion);
                _state.CoverStream = null;
                _state.CoverStreamReference = null;
            }
        }

        await _tileService.UpdateTile(providerItem, _uiSettings.NoImage ? null : _state.CoverStream);
        if (_lastFmSettings.UpdateLastFMNowPlaying)
            _taskRunner.Forget(_lastFm.UpdateNowPlayingAsync(providerItem), "update Last.FM now playing");
    }

    /// <inheritdoc />
    public async Task RefreshCoverAsync(SingleSongBase providerItem)
    {
        var requestVersion = Interlocked.Increment(ref _coverRequestVersion);
        InMemoryRandomAccessStream newStream = null;
        try
        {
            byte[]? bytes = null;
            var preload = Volatile.Read(ref _preloadedCover);
            if (preload is not null && preload.Matches(providerItem))
            {
                Interlocked.CompareExchange(ref _preloadedCover, null, preload);
                bytes = await preload.Bytes;
            }

            bytes ??= await DownloadCoverAsync(providerItem);
            if (bytes is null) return;
            if (requestVersion != Volatile.Read(ref _coverRequestVersion)) return;

            // 替换封面流
            newStream = new InMemoryRandomAccessStream();
            var buffer = bytes.AsBuffer();
            await newStream.WriteAsync(buffer);
            newStream.Seek(0);
            var newRef = RandomAccessStreamReference.CreateFromStream(newStream);
            lock (_coverPublishGate)
            {
                if (requestVersion != Volatile.Read(ref _coverRequestVersion)) return;
                _state.CoverStreamReference = newRef;
                _state.CoverStream = newStream;
                newStream = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Cover load failed: {ex.Message}");
        }
        finally
        {
            newStream?.Dispose();
        }
    }


    public void PreloadCover(SingleSongBase providerItem)
    {
        if (providerItem is null || _uiSettings.NoImage) return;
        if (Volatile.Read(ref _preloadedCover) is { } current && current.Matches(providerItem)) return;
        Volatile.Write(ref _preloadedCover, new CoverPreload(providerItem, DownloadCoverAsync(providerItem)));
    }

    private async Task<byte[]?> DownloadCoverAsync(SingleSongBase providerItem)
    {
        try
        {
            var coverUri = await GetCoverUriAsync(providerItem).ConfigureAwait(false);
            if (coverUri is null) return null;

            using var response = await _http.GetAsync(coverUri).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ScrobbleAsync(SingleSongBase providerItem)
    {
        if (providerItem == null) return;
        await _lastFm.ScrobbleAsync(providerItem);
    }

    private void UpdateSmtcDisplayInfo(SingleSongBase providerItem)
    {
        if (_player is AudioGraphPlayer { SMTCManager: not null } graphPlayer)
        {
            var title = providerItem.Name;
            var artist = providerItem.CreatorList is { Count: > 0 } creators
                ? string.Join(" / ", creators)
                : string.Empty;
            var album = providerItem.Album?.Name ?? string.Empty;
            var trackIdentity = SmtcTrackIdentity.Create(providerItem.ProviderId, providerItem.ActualId);

            graphPlayer.SMTCManager.UpdateDisplayInfo(
                title,
                artist,
                album,
                trackIdentity);
        }
    }

    private static async Task<Uri?> GetCoverUriAsync(SingleSongBase providerItem)
    {
        if (providerItem is not IHasCover coverProvider) return null;

        var coverResult = await coverProvider.GetCoverAsync(new NeteaseImageResourceQualityTag(CoverPixels, CoverPixels));
        if (coverResult is IResourceResultOf<Uri?> nullableUriResult)
            return await nullableUriResult.GetResourceAsync();
        if (coverResult is IResourceResultOf<Uri> uriResult)
            return await uriResult.GetResourceAsync();
        return null;
    }

    private sealed record CoverPreload(SingleSongBase Song, Task<byte[]?> Bytes)
    {
        public bool Matches(SingleSongBase song)
        {
            return ReferenceEquals(Song, song)
                   || Song.ProviderId == song.ProviderId
                   && !string.IsNullOrEmpty(song.ActualId)
                   && Song.ActualId == song.ActualId;
        }
    }

    private void UpdateSmtcThumbnail()
    {
        if (_state.CoverStreamReference is null) return;

        if (_player is AudioGraphPlayer { SMTCManager: not null } graphPlayer)
            graphPlayer.SMTCManager.UpdateThumbnail(_state.CoverStreamReference);
    }
}
