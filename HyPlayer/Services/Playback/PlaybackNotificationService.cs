using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.LastFM;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放通知服务 — 负责 SMTC 显示更新、磁贴刷新、封面下载和 Last.FM Scrobble。
/// </summary>
public sealed class PlaybackNotificationService : IPlaybackNotificationService
{
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly HttpClient _http;
    private readonly IPlayer _player;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly ITileService _tileService;

    public PlaybackNotificationService(
        PlaybackStateService state,
        Setting setting,
        HttpClient http,
        IPlayer player,
        IBackgroundTaskRunner taskRunner,
        ITileService tileService)
    {
        _state = state;
        _setting = setting;
        _http = http;
        _player = player;
        _taskRunner = taskRunner;
        _tileService = tileService;
    }

    /// <inheritdoc />
    public async Task OnTrackChangedAsync(SingleSongBase providerItem)
    {
        if (providerItem == null) return;
        UpdateSmtcDisplayInfo(providerItem);

        // 1. 刷新封面
        if (!_setting.noImage)
            await RefreshCoverAsync(providerItem);
        await _tileService.UpdateTile(providerItem, _state.CoverStream);
        if (!_setting.noImage)
            UpdateSmtcThumbnail();
        // 2. Last.FM now-playing
        if (_setting.UpdateLastFMNowPlaying)
        {
            _taskRunner.Forget(LastFMManager.UpdateNowPlaying(providerItem), "update Last.FM now playing");
        }
    }

    /// <inheritdoc />
    public async Task RefreshCoverAsync(SingleSongBase providerItem)
    {
        try
        {
            var coverUri = await GetCoverUriAsync(providerItem);
            if (coverUri is null) return;

            using var response = await _http.GetAsync(coverUri);
            if (!response.IsSuccessStatusCode) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            IBuffer buffer = bytes.AsBuffer();

            // 替换封面流
            var newStream = new InMemoryRandomAccessStream();
            await newStream.WriteAsync(buffer);
            var newRef = RandomAccessStreamReference.CreateFromStream(newStream);

            // Atomic swap
            var oldStream = _state.CoverStream;
            _state.CoverStreamReference = newRef;
            _state.CoverStream = newStream;
            oldStream?.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Cover load failed: {ex.Message}");
        }
    }


    /// <inheritdoc />
    public async Task ScrobbleAsync(SingleSongBase providerItem)
    {
        if (providerItem == null) return;
        await LastFMManager.Scrobble(providerItem);
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

            graphPlayer.SMTCManager.UpdateDisplayInfo(
                title,
                artist,
                album);
        }
    }

    private static async Task<Uri?> GetCoverUriAsync(SingleSongBase providerItem)
    {
        if (providerItem is not IHasCover coverProvider) return null;

        var coverResult = await coverProvider.GetCoverAsync(new ImageResourceQualityTag(1024, 1024));
        if (coverResult is IResourceResultOf<Uri?> nullableUriResult)
            return await nullableUriResult.GetResourceAsync();
        if (coverResult is IResourceResultOf<Uri> uriResult)
            return await uriResult.GetResourceAsync();
        return null;
    }

    private void UpdateSmtcThumbnail()
    {
        if (_state.CoverStreamReference is null) return;

        if (_player is AudioGraphPlayer { SMTCManager: not null } graphPlayer)
        {
            graphPlayer.SMTCManager.UpdateThumbnail(_state.CoverStreamReference);
        }
    }
}
