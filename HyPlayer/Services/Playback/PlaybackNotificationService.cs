using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
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
    public async Task OnTrackChangedAsync(HyPlayItem item)
    {
        if (item == null) return;
        UpdateSmtcDisplayInfo(item);

        _state.NowPlayingItem = item;

        // 1. 刷新封面
        if (!_setting.noImage)
            await RefreshCoverAsync(item);
        await _tileService.UpdateTile(item, _state.CoverStream);
        if (!_setting.noImage)
            UpdateSmtcThumbnail();
        // 2. Last.FM now-playing
        if (_setting.UpdateLastFMNowPlaying)
        {
            _taskRunner.Forget(LastFMManager.UpdateNowPlaying(item), "update Last.FM now playing");
        }
    }

    /// <inheritdoc />
    public async Task RefreshCoverAsync(HyPlayItem item)
    {
        try
        {
            IBuffer buffer;
            if (item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
            {
                buffer = item.PlayItem.CoverBuffer;
            }
            else
            {
                var coverUrl = item.Album?.Cover;
                if (string.IsNullOrEmpty(coverUrl)) return;

                var url = coverUrl + "?param=" + StaticSource.PICSIZE_AUDIO_PLAYER_COVER;
                using var response = await _http.GetAsync(new Uri(url));
                if (!response.IsSuccessStatusCode) return;

                var bytes = await response.Content.ReadAsByteArrayAsync();
                buffer = bytes.AsBuffer();
            }

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
    public async Task ScrobbleAsync(HyPlayItem item)
    {
        if (item == null) return;
        await LastFMManager.Scrobble(item);
    }

    private void UpdateSmtcDisplayInfo(HyPlayItem item)
    {
        if (_player is AudioGraphPlayer { SMTCManager: not null } graphPlayer)
        {
            var providerItem = _state.NowPlayingProviderItem;
            var title = providerItem?.Name ?? item.Name;
            var artist = providerItem?.CreatorList is { Count: > 0 } creators
                ? string.Join(" / ", creators)
                : item.ArtistString;
            var album = providerItem?.Album?.Name ?? item.AlbumString;

            graphPlayer.SMTCManager.UpdateDisplayInfo(
                title,
                artist,
                album);
        }
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
