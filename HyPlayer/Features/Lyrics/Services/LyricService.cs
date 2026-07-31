using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using ALRC.Converters;
using ALRC.Converters.Enhancers;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using LrcConverter = ALRC.Converters.LrcConverter;

namespace HyPlayer.Features.Lyrics.Services;

/// <summary>
///     歌词服务 — 负责歌词加载、缓存查询和逐行同步。
/// </summary>
public sealed class LyricService(
    ILyricProvidable lyricProvider,
    PlaybackStateService state,
    LyricSettings setting,
    HttpClient httpClient,
    IBackgroundTaskRunner taskRunner,
    IKawazuStateService kawazuState) : ILyricService
{
    /// <inheritdoc />
    public HyLyricInfo CurrentLyricInfo => state.LyricInfo;

    /// <inheritdoc />
    public int CurrentLyricIndex => state.LyricIndex;

    /// <inheritdoc />
    public TimeSpan LyricOffset { get; set; }

    /// <inheritdoc />
    public async Task LoadLyricsAsync(SingleSongBase providerItem, CancellationToken ct = default)
    {
        var cacheId = providerItem.ActualId;
        var canUseHyLyricInfoCache = !string.IsNullOrWhiteSpace(cacheId);
        var forceRefreshHyLyricInfoCache = false;
        if (canUseHyLyricInfoCache)
        {
            var cached = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, cacheId,
                () => Task.FromResult<HyLyricInfo>(null),
                cancellationToken: ct);

            if (cached is not null && HasDisplayableLyrics(cached, providerItem))
            {
                if (!HasLegacyNeteaseSourceMetadata(cached))
                {
                    state.LyricInfo = cached;
                    state.LyricIndex = 0;
                    return;
                }

                forceRefreshHyLyricInfoCache = true;
            }
        }

        var pureLyricInfo = providerItem switch
        {
            LocalSong localSong => await LoadLocalLyricAsync(localSong),
            _ => await LoadProviderLyricAsync(providerItem, ct)
        };
        var lyricInfo = await ConvertPureLyricInfoAsync(pureLyricInfo, GetArtistText(providerItem));

        state.LyricInfo = lyricInfo;
        state.LyricIndex = 0;

        if (canUseHyLyricInfoCache && HasCacheableLyrics(lyricInfo, providerItem))
            taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo, cacheId,
                    () => Task.FromResult(lyricInfo),
                    forceRefresh: forceRefreshHyLyricInfoCache,
                    cancellationToken: ct),
                "cache provider lyric info");

        await TryLoadAmllTtmlAsync(providerItem, ct);
    }

    /// <inheritdoc />
    public void Tick(TimeSpan position)
    {
        var lyrics = state.LyricInfo.Lyrics;
        if (lyrics == null || lyrics.Count == 0)
        {
            state.LyricIndex = 0;
            return;
        }

        var idx = state.LyricIndex;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
        }

        if (changed) state.LyricIndex = idx;
    }

    public async Task<HyLyricInfo?> ImportLyricsAsync(StorageFile lyricFile, SingleSongBase? currentSong,
        CancellationToken ct = default)
    {
        var content = await FileIO.ReadTextAsync(lyricFile).AsTask(ct);
        var lyricInfo = await ConvertAlrcLyricAsync(content, lyricFile.FileType, "本地歌词", lyricFile.Path);
        state.LyricInfo = lyricInfo;
        state.LyricIndex = 0;

        var cacheSongId = currentSong?.ActualId;
        if (!string.IsNullOrEmpty(cacheSongId))
            taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo,
                    cacheSongId,
                    () => Task.FromResult(lyricInfo),
                    forceRefresh: true,
                    cancellationToken: ct),
                "cache imported lyric info");

        return lyricInfo;
    }

    #region Private helpers

    private async Task<PureLyricInfo> LoadProviderLyricAsync(SingleSongBase providerItem, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(providerItem.ActualId))
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            var lyricResult = await lyricProvider.GetLyricInfoAsync(providerItem, ct);

            if (lyricResult is null)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 歌词获取失败" };

            if (lyricResult.Count == 0)
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            return await ConvertProviderLyricsAsync(lyricResult, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
            return new PureLyricInfo();
        }
    }

    private async Task<HyLyricInfo> ConvertPureLyricInfoAsync(PureLyricInfo pureLyricInfo, string artistText)
    {
        var lyricInfo = new HyLyricInfo();
        var karaokeLyricInfo = pureLyricInfo as KaraokLyricInfo;
        var hasKaraokeLyrics = !string.IsNullOrWhiteSpace(karaokeLyricInfo?.KaraokLyric);

        if (hasKaraokeLyrics)
            lyricInfo.Lyrics = Utils.ConvertKaraok(pureLyricInfo, setting.MigrateLyrics);
        else
            lyricInfo.Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics);

        if (lyricInfo.Lyrics.Count == 0)
        {
            if (setting.ShowComposerInLyric)
                lyricInfo.Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(artistText, TimeSpan.Zero)
                });
        }
        else
        {
            if (karaokeLyricInfo is null)
                Utils.ConvertTranslation(pureLyricInfo.TrLyrics, lyricInfo.Lyrics);
            else
                Utils.ConvertYrcTranslation(karaokeLyricInfo, lyricInfo.Lyrics, setting.MigrateLyrics);

            await Utils.ConvertRomaji(
                pureLyricInfo,
                lyricInfo.Lyrics,
                new Utils.LyricConversionOptions(
                    setting.MigrateLyrics,
                    setting.LyricRomajiSource,
                    kawazuState.Converter));

            if (lyricInfo.Lyrics.Count != 0 && lyricInfo.Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
                lyricInfo.Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, TimeSpan.Zero) });
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

    private static async Task<PureLyricInfo> ConvertProviderLyricsAsync(IEnumerable<RawLyricInfo> lyrics,
        CancellationToken ct)
    {
        static string CleanLyric(string? text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : string.Join('\n',
                    text.Replace("\r\n", "\n")
                        .Split('\n')
                        .Select(line => line.TrimEnd('\r'))
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Where(line => !line.TrimStart().StartsWith('{')));
        }

        ProviderLyricText? original = null;
        ProviderLyricText? translation = null;
        ProviderLyricText? romaji = null;
        ProviderLyricText? wordOriginal = null;
        ProviderLyricText? wordTranslation = null;
        ProviderLyricText? wordRomaji = null;

        foreach (var lyric in lyrics)
        {
            var resource = await lyric.GetResourceAsync(ctk: ct);
            var text = resource is IResourceResultOf<string> textResource
                ? await textResource.GetResourceAsync(ct)
                : null;

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var lyricText = new ProviderLyricText(lyric, text);
            var isWordLyric = lyric.Source?.Contains("yrc", StringComparison.OrdinalIgnoreCase) is true;
            switch (lyric.LyricType)
            {
                case LyricType.Original when isWordLyric && wordOriginal is null:
                    wordOriginal = lyricText;
                    break;
                case LyricType.Translation when isWordLyric && wordTranslation is null:
                    wordTranslation = lyricText;
                    break;
                case LyricType.Romaji when isWordLyric && wordRomaji is null:
                    wordRomaji = lyricText;
                    break;
                case LyricType.Original when original is null:
                    original = lyricText;
                    break;
                case LyricType.Translation when translation is null:
                    translation = lyricText;
                    break;
                case LyricType.Romaji when romaji is null:
                    romaji = lyricText;
                    break;
            }
        }

        var result = wordOriginal is null
            ? new PureLyricInfo
            {
                PureLyrics = CleanLyric(original?.Text),
                TrLyrics = CleanLyric(translation?.Text),
                NeteaseRomaji = CleanLyric(romaji?.Text)
            }
            : new KaraokLyricInfo
            {
                PureLyrics = CleanLyric(original?.Text),
                TrLyrics = CleanLyric(translation?.Text),
                NeteaseRomaji = CleanLyric(romaji?.Text),
                KaraokLyric = CleanLyric(wordOriginal.Text),
                YrTrLyrics = CleanLyric(wordTranslation?.Text),
                YrNeteaseRomaji = CleanLyric(wordRomaji?.Text)
            };

        var displayedOriginal = wordOriginal ?? original;
        var displayedTranslation = wordTranslation ?? translation;
        AddProviderContributorMetadata(result, displayedOriginal?.Info, "lyric_user", "歌词贡献者");
        AddProviderContributorMetadata(result, displayedTranslation?.Info, "translation_user", "翻译贡献者");

        if (result.LyricMetadata.Count == 0)
            AddLyricMetadata(
                result,
                displayedOriginal?.Info.Source ??
                displayedTranslation?.Info.Source ?? wordRomaji?.Info.Source ?? romaji?.Info.Source,
                "source",
                "歌词来源");

        return result;
    }

    private static void AddProviderContributorMetadata(PureLyricInfo result, RawLyricInfo? lyric, string key,
        string displayName)
    {
        if (lyric is not NeteaseRawLyricInfo { Author: { } author } ||
            string.IsNullOrWhiteSpace(author.Name))
            return;

        var actionUri = string.IsNullOrWhiteSpace(author.ActualId)
            ? null
            : $"hyplayer://us{author.ActualId}";
        AddLyricMetadata(result, author.Name, key, displayName, actionUri);
    }

    private static void AddLyricMetadata(PureLyricInfo result, string? value, string key, string displayName,
        string? actionUri = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        result.LyricMetadata.Add(new LyricInfoMetadata
        {
            Key = key,
            Value = value,
            DisplayName = displayName,
            ActionUri = actionUri
        });
    }

    private sealed record ProviderLyricText(RawLyricInfo Info, string Text);

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
            return new PureLyricInfo();
        }
    }

    private async Task TryLoadAmllTtmlAsync(SingleSongBase item, CancellationToken ct)
    {
        try
        {
            if (!setting.EnableAmllTtmlDb || string.IsNullOrWhiteSpace(item.ActualId)) return;

            using var message = new HttpRequestMessage(HttpMethod.Get,
                setting.AmllTtmlMirrorUrl.Replace("[NCM_ID]", item.ActualId));
            message.Headers.Add("User-Agent", "HyPlayer LyricsClient");
            using var ttml = await httpClient.SendAsync(message, ct);
            var ttmlContent = await ttml.Content.ReadAsStringAsync(ct);
            var importedLyric = await ConvertAlrcLyricAsync(
                ttmlContent,
                ".ttml",
                "amll-ttml-db",
                $"https://github.com/amll-dev/amll-ttml-db/blob/main/ncm-lyrics/{item.ActualId}.ttml",
                true);
            state.LyricInfo = importedLyric;
            state.LyricIndex = 0;

            if (HasCacheableLyrics(importedLyric, item))
                taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                        CacheType.HyLyricInfo, item.ActualId,
                        () => Task.FromResult(importedLyric),
                        forceRefresh: true,
                        cancellationToken: ct),
                    "refresh AMLL lyric cache");
        }
        catch
        {
        }
    }

    private static Task<HyLyricInfo> ConvertAlrcLyricAsync(
        string lyricText,
        string fileType,
        string sourceName,
        string? sourceUri,
        bool includeAuthorMetadata = false)
    {
        var converter = CreateAlrcConverter(fileType);
        var lrcConverter = new LrcConverter();
        var lrcTranslationConverter = new LrcTranslationEnhancer();
        var alrc = converter.Convert(lyricText);
        var lrc = lrcConverter.ConvertBack(alrc);
        var trLrc = lrcTranslationConverter.Extract(alrc);
        var metadata = new List<LyricInfoMetadata>();

        if (includeAuthorMetadata && !string.IsNullOrWhiteSpace(alrc.LyricInfo?.Author))
            metadata.Add(new LyricInfoMetadata
            {
                Key = "lyric_user",
                Value = alrc.LyricInfo.Author,
                DisplayName = "歌词作者",
                ActionUri = $"https://github.com/{alrc.LyricInfo.Author}"
            });

        metadata.Add(new LyricInfoMetadata
        {
            Key = "source",
            Value = sourceName,
            DisplayName = "歌词来源",
            ActionUri = sourceUri
        });

        var pureLyricInfo = new HyALRCLyricInfo
        {
            PureLyrics = lrc,
            TrLyrics = trLrc,
            ALRC = alrc,
            LyricMetadata = metadata,
            SongMetadata = []
        };

        var lyricInfo = new HyLyricInfo
        {
            LyricMetadata = pureLyricInfo.LyricMetadata,
            PureLyricInfo = pureLyricInfo,
            SongMetadata = pureLyricInfo.SongMetadata,
            Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics)
        };
        Utils.ConvertTranslation(pureLyricInfo.TrLyrics, lyricInfo.Lyrics);
        return Task.FromResult(lyricInfo);
    }

    private static ILyricConverter<string> CreateAlrcConverter(string fileType)
    {
        return fileType switch
        {
            ".qrc" => new QQLyricConverter(),
            ".yrc" => new NeteaseYrcConverter(),
            ".lrc" => new LrcConverter(),
            ".alrc" => new ALRCConverter(),
            ".ttml" => new AppleSyllableConverter(),
            ".lys" => new LyricifySyllableConverter(),
            _ => throw new NotImplementedException()
        };
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

    private static bool HasLegacyNeteaseSourceMetadata(HyLyricInfo lyricInfo)
    {
        return lyricInfo.LyricMetadata.Any(t =>
            string.Equals(t.Key, "source", StringComparison.Ordinal) &&
            t.Value?.StartsWith("netease:", StringComparison.OrdinalIgnoreCase) is true);
    }

    #endregion
}
