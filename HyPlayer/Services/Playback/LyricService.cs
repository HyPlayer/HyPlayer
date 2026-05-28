using ALRC.Converters;
using ALRC.Converters.Enhancers;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using LrcConverter = ALRC.Converters.LrcConverter;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 歌词服务 — 负责歌词加载、缓存查询和逐行同步。
/// </summary>
public sealed class LyricService : ILyricService
{
    public event EventHandler<LyricLoadedEventArgs>? LyricLoaded;
    public event EventHandler<LyricIndexChangedEventArgs>? LyricIndexChanged;

    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly IBackgroundTaskRunner _taskRunner;
    private IPlayer _player;

    public LyricService(
        NeteaseCloudMusicApiHandler api,
        PlaybackStateService state,
        Setting setting,
        HttpClient httpClient,
        IBackgroundTaskRunner taskRunner,
        IPlayer player)
    {
        _api = api;
        _state = state;
        _setting = setting;
        _httpClient = httpClient;
        _taskRunner = taskRunner;
        _player = player;
        
    }

    /// <inheritdoc />
    public HyLyricInfo CurrentLyricInfo => _state.LyricInfo;

    /// <inheritdoc />
    public int CurrentLyricIndex => _state.LyricIndex;

    /// <inheritdoc />
    public TimeSpan LyricOffset { get; set; }

    /// <inheritdoc />
    public async Task LoadLyricsAsync(HyPlayItem item, CancellationToken ct = default)
    {
        // 1. 尝试从缓存获取
        var canUseHyLyricInfoCache = item.ItemType == HyPlayItemType.Netease && !string.IsNullOrWhiteSpace(item.Id);
        if (canUseHyLyricInfoCache)
        {
            var cached = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, item.Id,
                () => Task.FromResult<HyLyricInfo>(null),
                cancellationToken: ct);

            if (cached is not null && HasDisplayableLyrics(cached, item))
            {
                _state.LyricInfo = cached;
                _state.LyricIndex = 0;
                LyricLoaded?.Invoke(this, new LyricLoadedEventArgs(cached));
                return;
            }
        }

        // 2. 根据类型加载原始歌词
        var pureLyricInfo = item.ItemType switch
        {
            HyPlayItemType.Netease => await LoadNcLyricAsync(item, ct),
            HyPlayItemType.Local => await LoadLocalLyricAsync(item),
            _ => new PureLyricInfo()
        };

        // 3. 转换歌词行
        var lyricInfo = new HyLyricInfo();

        if (pureLyricInfo is KaraokLyricInfo)
        {
            lyricInfo.Lyrics = Utils.ConvertKaraok(pureLyricInfo);
        }
        else
        {
            lyricInfo.Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics);
        }

        // 4. 空歌词时显示歌手名
        if (lyricInfo.Lyrics.Count == 0)
        {
            if (_setting.showComposerInLyric)
            {
                lyricInfo.Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(item.ArtistString, TimeSpan.Zero)
                });
            }
        }
        else
        {
            // 翻译 & 罗马音
            if (pureLyricInfo is not KaraokLyricInfo)
                Utils.ConvertTranslation(pureLyricInfo.TrLyrics, lyricInfo.Lyrics);
            else
                Utils.ConvertYrcTranslation((KaraokLyricInfo)pureLyricInfo, lyricInfo.Lyrics);

            await Utils.ConvertRomaji(pureLyricInfo, lyricInfo.Lyrics);

            // 确保首行从 0 开始
            if (lyricInfo.Lyrics.Count != 0 && lyricInfo.Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
            {
                lyricInfo.Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, TimeSpan.Zero) });
            }
        }

        lyricInfo.LyricMetadata = pureLyricInfo.LyricMetadata;
        lyricInfo.SongMetadata = pureLyricInfo.SongMetadata;
        lyricInfo.PureLyricInfo = pureLyricInfo;

        _state.LyricInfo = lyricInfo;
        _state.LyricIndex = 0;

        LyricLoaded?.Invoke(this, new LyricLoadedEventArgs(lyricInfo));

        // 5. 写入缓存
        if (canUseHyLyricInfoCache && HasCacheableLyrics(lyricInfo, item))
        {
            _taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, item.Id,
                () => Task.FromResult(lyricInfo),
                cancellationToken: ct),
                "cache lyric info");
        }

        // 6. 尝试加载 AMLL TTML 歌词（覆盖）
        await TryLoadAmllTtmlAsync(item, lyricInfo, ct);
    }

    /// <inheritdoc />
    public void Tick(TimeSpan position)
    {
        var lyrics = _state.LyricInfo.Lyrics;
        if (lyrics == null || lyrics.Count == 0)
        {
            _state.LyricIndex = 0;
            return;
        }

        var idx = _state.LyricIndex;
        if (idx >= lyrics.Count || idx < 0) idx = 0;

        var realPos = position - LyricOffset;
        var changed = false;

        // 进度回溯
        if (lyrics[idx].LyricLine.StartTime > realPos)
        {
            idx = lyrics.FindLastIndex(t => t.LyricLine.StartTime <= realPos) - 1;
            if (idx == -2) idx = -1;
            changed = true;
        }

        // 正常滚动
        try
        {
            if (idx == 0 && lyrics.Count != 1) changed = false;
            while (lyrics.Count > idx + 1 && lyrics[idx + 1].LyricLine.StartTime <= realPos)
            {
                idx++;
                changed = true;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lyric error: {ex.Message}");
        }

        if (changed)
        {
            _state.LyricIndex = idx;
            LyricIndexChanged?.Invoke(this, new LyricIndexChangedEventArgs(idx));
        }
    }

    #region Private helpers

    private async Task<PureLyricInfo> LoadNcLyricAsync(HyPlayItem item, CancellationToken ct)
    {
        try
        {
            if (item.ItemType != HyPlayItemType.Netease || item.PlayItem == null || string.IsNullOrWhiteSpace(item.Id))
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            var lyricResult = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.LyricApi, item.Id,
                async () =>
                {
                    var resp = await _api.RequestAsync(NeteaseApis.LyricApi, new LyricRequest { Id = item.Id });
                    return resp.IsError ? null : resp.Value;
                },
                cancellationToken: ct);

            if (lyricResult is null)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 歌词获取失败" };

            if (lyricResult.Lyric is null && lyricResult.YunLyric is null)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            static string CleanLrc(string text) =>
                string.IsNullOrEmpty(text)
                    ? string.Empty
                    : string.Join('\n', text.Split("\n").Where(t => !t.StartsWith('{')).ToArray());

            PureLyricInfo res;

            if (lyricResult.YunLyric?.Lyric is null)
            {
                res = new PureLyricInfo
                {
                    PureLyrics = CleanLrc(lyricResult.Lyric?.Lyric),
                    TrLyrics = lyricResult.TranslationLyric?.Lyric,
                    NeteaseRomaji = lyricResult.RomajiLyric?.Lyric,
                };
            }
            else
            {
                res = new KaraokLyricInfo
                {
                    PureLyrics = CleanLrc(lyricResult.Lyric?.Lyric),
                    TrLyrics = lyricResult.TranslationLyric?.Lyric,
                    NeteaseRomaji = lyricResult.RomajiLyric?.Lyric,
                    KaraokLyric = CleanLrc(lyricResult.YunLyric.Lyric),
                    YrTrLyrics = lyricResult.YunTranslationLyric?.Lyric,
                    YrNeteaseRomaji = lyricResult.YunRomajiLyric?.Lyric,
                };
            }

            // metadata
            if (lyricResult.LyricUser?.UserId is not null)
            {
                res.LyricMetadata.Add(new LyricInfoMetadata
                {
                    Key = "lyric_user",
                    Value = lyricResult.LyricUser.Nickname,
                    ActionUri = $"hyplayer://us{lyricResult.LyricUser.UserId}",
                    DisplayName = "歌词贡献者"
                });
            }

            if (lyricResult.TranslationUser?.UserId is not null)
            {
                res.LyricMetadata.Add(new LyricInfoMetadata
                {
                    Key = "translation_user",
                    Value = lyricResult.TranslationUser.Nickname,
                    ActionUri = $"hyplayer://us{lyricResult.TranslationUser.UserId}",
                    DisplayName = "翻译贡献者"
                });
            }

            return res;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lyric error: {ex.Message}");
            return new PureLyricInfo();
        }
    }

    private static async Task<PureLyricInfo> LoadLocalLyricAsync(HyPlayItem item)
    {
        try
        {
            var lrcPath = Path.ChangeExtension(item.Url, "lrc");
            var file = await StorageFile.GetFileFromPathAsync(lrcPath);
            var text = await FileIO.ReadTextAsync(file);
            return new PureLyricInfo { PureLyrics = text };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lyric error: {ex.Message}");
            return new PureLyricInfo();
        }
    }

    private async Task TryLoadAmllTtmlAsync(HyPlayItem item, HyLyricInfo lyricInfo, CancellationToken ct)
    {
        try
        {
            if (!_setting.enableAmllTtmlDb || item.ItemType != HyPlayItemType.Netease || string.IsNullOrWhiteSpace(item.Id)) return;

            using var message = new HttpRequestMessage(HttpMethod.Get, _setting.amllTtmlMirrorUrl.Replace("[NCM_ID]", item.Id));
            message.Headers.Add("User-Agent", "HyPlayer LyricsClient");
            using var ttml = await _httpClient.SendAsync(message, ct);
            var ttmlContent = await ttml.Content.ReadAsStringAsync(ct);
            var ttmlConverter = new AppleSyllableConverter();
            var lrcConverter = new LrcConverter();
            var lrcTranslationConverter = new LrcTranslationEnhancer();
            var alrc = ttmlConverter.Convert(ttmlContent);
            var lrc = lrcConverter.ConvertBack(alrc);
            var trLrc = lrcTranslationConverter.Extract(alrc);

            var ttmlLyric = new HyALRCLyricInfo
            {
                PureLyrics = lrc,
                TrLyrics = trLrc,
                ALRC = alrc,
                LyricMetadata =
                [
                    new LyricInfoMetadata
                    {
                        Key = "lyric_user",
                        Value = alrc.LyricInfo?.Author,
                        DisplayName = "歌词作者",
                        ActionUri = $"https://github.com/{alrc.LyricInfo?.Author}"
                    },
                    new LyricInfoMetadata
                    {
                        Key = "source",
                        Value = "amll-ttml-db",
                        DisplayName = "歌词来源",
                        ActionUri = $"https://github.com/amll-dev/amll-ttml-db/blob/main/ncm-lyrics/{item.Id}.ttml"
                    }
                ],
                SongMetadata = []
            };

            lyricInfo.Lyrics = Utils.ConvertPureLyric(ttmlLyric.PureLyrics);
            Utils.ConvertTranslation(ttmlLyric.TrLyrics, lyricInfo.Lyrics);
            lyricInfo.LyricMetadata = ttmlLyric.LyricMetadata;
            lyricInfo.SongMetadata = ttmlLyric.SongMetadata;
            lyricInfo.PureLyricInfo = ttmlLyric;

        LyricLoaded?.Invoke(this, new LyricLoadedEventArgs(lyricInfo));

            if (HasCacheableLyrics(lyricInfo, item))
            {
                _taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo, item.Id,
                    () => Task.FromResult(lyricInfo),
                    forceRefresh: true,
                    cancellationToken: ct),
                    "refresh AMLL lyric cache");
            }
        }
        catch
        {

        }
    }

    private static bool HasDisplayableLyrics(HyLyricInfo lyricInfo, HyPlayItem item)
    {
        return lyricInfo.Lyrics.Any(t =>
            !string.IsNullOrWhiteSpace(t.LyricLine.CurrentLyric) &&
            !string.Equals(t.LyricLine.CurrentLyric, item.ArtistString, StringComparison.Ordinal));
    }

    private static bool HasCacheableLyrics(HyLyricInfo lyricInfo, HyPlayItem item)
    {
        return HasDisplayableLyrics(lyricInfo, item);
    }

    #endregion
}
