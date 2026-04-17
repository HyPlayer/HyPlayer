using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
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

    public PlaybackNotificationService(PlaybackStateService state, Setting setting, HttpClient http)
    {
        _state = state;
        _setting = setting;
        _http = http;
    }

    /// <inheritdoc />
    public async Task OnTrackChangedAsync(HyPlayItem item)
    {
        if (item == null) return;

        // 1. 刷新封面
        if (!_setting.noImage)
        {
            await RefreshCoverAsync(item);
            WeakReferenceMessenger.Default.Send(new CoverChangedMessage(item));
        }

        // 2. Last.FM now-playing
        if (_setting.UpdateLastFMNowPlaying)
        {
            _ = LastFMManager.UpdateNowPlaying(item);
        }
    }

    /// <inheritdoc />
    public async Task RefreshCoverAsync(HyPlayItem item)
    {
        try
        {
            if (item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
            {
                // 本地文件封面由旧逻辑处理（需要 StorageFile 访问），
                // 新服务仅处理网络曲目封面下载。
                return;
            }

            var coverUrl = item.Album?.Cover;
            if (string.IsNullOrEmpty(coverUrl)) return;

            var url = coverUrl + "?param=" + StaticSource.PICSIZE_AUDIO_PLAYER_COVER;
            using var response = await _http.GetAsync(new Uri(url));
            if (!response.IsSuccessStatusCode) return;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var buffer = bytes.AsBuffer();

            // 替换封面流
            var newStream = new InMemoryRandomAccessStream();
            await newStream.WriteAsync(buffer);
            var newRef = RandomAccessStreamReference.CreateFromStream(newStream);

            // Atomic swap
            var oldStream = _state.CoverStream;
            _state.CoverStream = newStream;
            _state.CoverStreamReference = newRef;
            oldStream?.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Cover load failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void UpdateSmtcPosition(TimeSpan position, TimeSpan duration)
    {
        _state.Position = position;
        _state.Duration = duration;
    }

    /// <inheritdoc />
    public async Task ScrobbleAsync(HyPlayItem item)
    {
        if (item == null) return;
        await LastFMManager.Scrobble(item);
    }
}
