#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using Windows.Media.Core;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// <c>nst</c> — 网易云纯流式播放提供者（不缓存）。
/// <para>
/// 通过网易云 API 获取播放链接后，直接使用 <see cref="MediaSource.CreateFromUri"/> 创建媒体源。
/// 播放链接通过 <see cref="SimpleCacher"/> 缓存 20 分钟。
/// </para>
/// </summary>
public sealed class NeteaseStreamingProvider : IMediaSourceProvider
{
    private const string SongUrlCacheKeyFormat = "{0}_{1}";
    private const int SongUrlCacheMinutes = 20;

    private readonly Setting _setting;
    private readonly NeteaseCloudMusicApiHandler _neteaseApi;

    /// <inheritdoc />
    public string Id => "nst";

    /// <summary>
    /// 创建 <see cref="NeteaseStreamingProvider"/> 实例
    /// </summary>
    /// <param name="setting">应用设置，用于获取音质配置</param>
    /// <param name="neteaseApi">网易云 API 处理器，用于获取播放链接</param>
    public NeteaseStreamingProvider(Setting setting, NeteaseCloudMusicApiHandler neteaseApi)
    {
        _setting = setting;
        _neteaseApi = neteaseApi;
    }

    /// <inheritdoc />
    public async Task<MediaSource?> CreateAsync(HyPlayItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var (playUrl, _) = await GetPlayUrlAsync(item, ct);
        if (playUrl == null)
            return null;

        return MediaSource.CreateFromUri(new Uri(playUrl));
    }

    /// <summary>
    /// 获取歌曲播放链接，支持 SimpleCacher 缓存（20 分钟 TTL）
    /// </summary>
    private async Task<(string? Url, long Size)> GetPlayUrlAsync(HyPlayItem item, CancellationToken ct)
    {
        var playUrl = item.Url;
        var size = item.Size;

        if ((string.IsNullOrEmpty(item.Url) || _setting.songUrlLazyGet) && item.Id != "-1")
        {
            var songResult = await RetryPolicies.UrlFetchPolicy.ExecuteAsync(async () =>
            {
                var result = await SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.SongUrl,
                    string.Format(SongUrlCacheKeyFormat, item.Id, _setting.audioRate),
                    async () =>
                    {
                        ct.ThrowIfCancellationRequested();

                        var songRequest = new SongUrlRequest
                        {
                            Level = _setting.audioRate,
                            Id = item.Id
                        };

                        var songRes = await _neteaseApi.RequestAsync(
                            NeteaseApis.SongUrlApi, songRequest);

                        if (songRes.IsError && songRes.Error != null)
                        {
                            throw songRes.Error;
                        }

                        return songRes.Value;
                    },
                    TimeSpan.FromMinutes(SongUrlCacheMinutes),
                    cancellationToken: ct);

                return result ?? throw new InvalidOperationException("下载链接获取失败");
            });

            if (songResult?.SongUrls?[0].Code == 200)
            {
                if (songResult.SongUrls[0].FreeTrialInfo is not null && _setting.jumpVipSongPlaying)
                {
                    return (null, 0);
                }

                playUrl = songResult.SongUrls[0].Url;
                size = songResult.SongUrls[0].Size;

                if (_setting.UseHttpWhenGettingSongs && (playUrl?.Contains("https://") ?? false))
                {
                    playUrl = playUrl.Replace("https://", "http://");
                }
            }
            else
            {
                throw new InvalidOperationException("下载链接获取失败");
            }
        }

        return (playUrl, size);
    }
}
