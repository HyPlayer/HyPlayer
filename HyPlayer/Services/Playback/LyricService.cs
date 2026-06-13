using ALRC.Converters;
using ALRC.Converters.Enhancers;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Playback.LocalProvider;
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

    private readonly ILyricProvidable _lyricProvider;
    private readonly PlaybackStateService _state;
    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly IBackgroundTaskRunner _taskRunner;

    public LyricService(
        ILyricProvidable lyricProvider,
        PlaybackStateService state,
        Setting setting,
        HttpClient httpClient,
        IBackgroundTaskRunner taskRunner)
    {
        _lyricProvider = lyricProvider;
        _state = state;
        _setting = setting;
        _httpClient = httpClient;
        _taskRunner = taskRunner;
    }

    /// <inheritdoc />
    public HyLyricInfo CurrentLyricInfo => _state.LyricInfo;

    /// <inheritdoc />
    public int CurrentLyricIndex => _state.LyricIndex;

    /// <inheritdoc />
    public TimeSpan LyricOffset { get; set; }

    /// <inheritdoc />
    public async Task LoadLyricsAsync(SingleSongBase providerItem, CancellationToken ct = default)
    {
        var cacheId = providerItem.ActualId;
        var canUseHyLyricInfoCache = providerItem is NeteaseSong && !string.IsNullOrWhiteSpace(cacheId);
        if (canUseHyLyricInfoCache)
        {
            var cached = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, cacheId,
                () => Task.FromResult<HyLyricInfo>(null),
                cancellationToken: ct);

            if (cached is not null && HasDisplayableLyrics(cached, providerItem))
            {
                _state.LyricInfo = cached;
                _state.LyricIndex = 0;
                LyricLoaded?.Invoke(this, new LyricLoadedEventArgs(cached));
                return;
            }
        }

        var pureLyricInfo = providerItem switch
        {
            NeteaseSong => await LoadNcLyricAsync(providerItem, ct),
            LocalSong localSong => await LoadLocalLyricAsync(localSong),
            _ => new PureLyricInfo()
        };
        var lyricInfo = await ConvertPureLyricInfoAsync(pureLyricInfo, GetArtistText(providerItem));

        _state.LyricInfo = lyricInfo;
        _state.LyricIndex = 0;

        LyricLoaded?.Invoke(this, new LyricLoadedEventArgs(lyricInfo));

        if (canUseHyLyricInfoCache && HasCacheableLyrics(lyricInfo, providerItem))
        {
            _taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, cacheId,
                () => Task.FromResult(lyricInfo),
                cancellationToken: ct),
                "cache provider lyric info");
        }

        if (providerItem is NeteaseSong)
            await TryLoadAmllTtmlAsync(providerItem, lyricInfo, ct);
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

    private async Task<PureLyricInfo> LoadNcLyricAsync(SingleSongBase providerItem, CancellationToken ct)
    {
        try
        {
            if (providerItem is not NeteaseSong || string.IsNullOrWhiteSpace(providerItem.ActualId))
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            var lyricResult = await _lyricProvider.GetLyricInfoAsync(providerItem, ct);

            if (lyricResult is null)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 歌词获取失败" };

            if (lyricResult.Count == 0)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            return ConvertProviderLyrics(lyricResult);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lyric error: {ex.Message}");
            return new PureLyricInfo();
        }
    }

    private async Task<HyLyricInfo> ConvertPureLyricInfoAsync(PureLyricInfo pureLyricInfo, string artistText)
    {
        var lyricInfo = new HyLyricInfo();
        var karaokeLyricInfo = pureLyricInfo as KaraokLyricInfo;
        var hasKaraokeLyrics = !string.IsNullOrWhiteSpace(karaokeLyricInfo?.KaraokLyric);

        if (hasKaraokeLyrics)
        {
            lyricInfo.Lyrics = Utils.ConvertKaraok(pureLyricInfo);
        }
        else
        {
            lyricInfo.Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics);
        }

        if (lyricInfo.Lyrics.Count == 0)
        {
            if (_setting.showComposerInLyric)
            {
                lyricInfo.Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(artistText, TimeSpan.Zero)
                });
            }
        }
        else
        {
            if (karaokeLyricInfo is null)
                Utils.ConvertTranslation(pureLyricInfo.TrLyrics, lyricInfo.Lyrics);
            else
                Utils.ConvertYrcTranslation(karaokeLyricInfo, lyricInfo.Lyrics);

            await Utils.ConvertRomaji(pureLyricInfo, lyricInfo.Lyrics);

            if (lyricInfo.Lyrics.Count != 0 && lyricInfo.Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
            {
                lyricInfo.Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, TimeSpan.Zero) });
            }
        }

        lyricInfo.LyricMetadata = pureLyricInfo.LyricMetadata;
        lyricInfo.SongMetadata = pureLyricInfo.SongMetadata;
        lyricInfo.PureLyricInfo = pureLyricInfo;
        return lyricInfo;
    }

    private static string GetArtistText(SingleSongBase providerItem)
    {
        return providerItem.CreatorList is { Count: > 0 } creators
            ? string.Join("; ", creators)
            : string.Empty;
    }

    private static PureLyricInfo ConvertProviderLyrics(System.Collections.Generic.IEnumerable<RawLyricInfo> lyrics)
    {
        static string CleanLyric(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : string.Join('\n',
                    text.Replace("\r\n", "\n")
                        .Split('\n')
                        .Select(line => line.TrimEnd('\r'))
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Where(line => !line.TrimStart().StartsWith('{')));

        static NeteaseRawLyricInfo? FirstNonEmpty(
            System.Collections.Generic.IEnumerable<NeteaseRawLyricInfo> items,
            LyricType type) =>
            items.FirstOrDefault(lyric => lyric.LyricType == type && !string.IsNullOrWhiteSpace(lyric.LyricText));

        var providerLyrics = lyrics.OfType<NeteaseRawLyricInfo>()
            .Where(lyric => !string.IsNullOrWhiteSpace(lyric.LyricText))
            .ToList();
        var wordLyrics = providerLyrics.Where(lyric => lyric.IsWord).ToList();
        var normalLyrics = providerLyrics.Where(lyric => !lyric.IsWord).ToList();

        var original = FirstNonEmpty(normalLyrics, LyricType.Original);
        var translation = FirstNonEmpty(normalLyrics, LyricType.Translation);
        var romaji = FirstNonEmpty(normalLyrics, LyricType.Romaji);
        var wordOriginal = FirstNonEmpty(wordLyrics, LyricType.Original);
        var wordTranslation = FirstNonEmpty(wordLyrics, LyricType.Translation);
        var wordRomaji = FirstNonEmpty(wordLyrics, LyricType.Romaji);

        PureLyricInfo result = wordOriginal is null
            ? new PureLyricInfo
            {
                PureLyrics = CleanLyric(original?.LyricText),
                TrLyrics = CleanLyric(translation?.LyricText),
                NeteaseRomaji = CleanLyric(romaji?.LyricText),
            }
            : new KaraokLyricInfo
            {
                PureLyrics = CleanLyric(original?.LyricText),
                TrLyrics = CleanLyric(translation?.LyricText),
                NeteaseRomaji = CleanLyric(romaji?.LyricText),
                KaraokLyric = CleanLyric(wordOriginal.LyricText),
                YrTrLyrics = CleanLyric(wordTranslation?.LyricText),
                YrNeteaseRomaji = CleanLyric(wordRomaji?.LyricText),
            };

        AddLyricMetadata(result, original, "lyric_user", "歌词贡献者");
        AddLyricMetadata(result, translation, "translation_user", "翻译贡献者");
        return result;
    }

    private static void AddLyricMetadata(PureLyricInfo result, NeteaseRawLyricInfo? lyric, string key, string displayName)
    {
        if (lyric?.Author?.ActualId is null) return;
        result.LyricMetadata.Add(new LyricInfoMetadata
        {
            Key = key,
            Value = lyric.Author.Name,
            ActionUri = $"hyplayer://us{lyric.Author.ActualId}",
            DisplayName = displayName
        });
    }

    private static async Task<PureLyricInfo> LoadLocalLyricAsync(LocalSong item)
    {
        try
        {
            var path = item.StorageFile?.Path ?? item.ActualId;
            if (string.IsNullOrWhiteSpace(path))
                return new PureLyricInfo();

            var lrcPath = Path.ChangeExtension(path, "lrc");
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

    private async Task TryLoadAmllTtmlAsync(SingleSongBase item, HyLyricInfo lyricInfo, CancellationToken ct)
    {
        try
        {
            if (!_setting.enableAmllTtmlDb || item is not NeteaseSong || string.IsNullOrWhiteSpace(item.ActualId)) return;

            using var message = new HttpRequestMessage(HttpMethod.Get, _setting.amllTtmlMirrorUrl.Replace("[NCM_ID]", item.ActualId));
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
                        ActionUri = $"https://github.com/amll-dev/amll-ttml-db/blob/main/ncm-lyrics/{item.ActualId}.ttml"
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
                    CacheType.HyLyricInfo, item.ActualId,
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

    private static bool HasDisplayableLyrics(HyLyricInfo lyricInfo, SingleSongBase providerItem)
    {
        var artistText = GetArtistText(providerItem);
        return lyricInfo.Lyrics.Any(t =>
            !string.IsNullOrWhiteSpace(t.LyricLine.CurrentLyric) &&
            !string.Equals(t.LyricLine.CurrentLyric, artistText, StringComparison.Ordinal));
    }

    private static bool HasCacheableLyrics(HyLyricInfo lyricInfo, SingleSongBase providerItem)
    {
        return HasDisplayableLyrics(lyricInfo, providerItem);
    }

    #endregion
}
