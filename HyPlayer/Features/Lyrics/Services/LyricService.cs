using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
using HyPlayer.Platform.Storage.Audio;
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
public sealed partial class LyricService(
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

    private readonly object _loadSync = new();
    private LyricLoad? _currentLoad;
    private LyricLoad? _preloadedLoad;

    /// <inheritdoc />
    public async Task LoadLyricsAsync(SingleSongBase providerItem, CancellationToken ct = default)
    {
        LyricLoad load;
        int version;
        lock (_loadSync)
        {
            if (_currentLoad is { } current && !(current.Reusable && current.Matches(providerItem)))
            {
                current.Cancel();
                _currentLoad = null;
            }

            if (_currentLoad is null && _preloadedLoad is { } preloaded && preloaded.Matches(providerItem))
            {
                _preloadedLoad = null;
                if (preloaded.Reusable) _currentLoad = preloaded;
                else preloaded.Cancel();
            }

            load = _currentLoad ??= StartLoad(providerItem);
            version = load.Attach(PublishLyrics, PublishPendingLyrics);
        }

        try
        {
            await load.Completion.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            load.Detach(version);
            throw;
        }
    }

    public void PreloadLyrics(SingleSongBase providerItem)
    {
        lock (_loadSync)
        {
            if (_currentLoad is { Reusable: true } current && current.Matches(providerItem)) return;
            if (_preloadedLoad is { } preloaded)
            {
                if (preloaded.Reusable && preloaded.Matches(providerItem)) return;
                preloaded.Cancel();
            }

            _preloadedLoad = StartLoad(providerItem);
        }
    }

    private LyricLoad StartLoad(SingleSongBase providerItem)
    {
        var load = new LyricLoad(providerItem, (owner, token) => ResolveLyricsAsync(providerItem, owner, token));
        taskRunner.Forget(load.Completion, "resolve lyrics");
        return load;
    }

    private void PublishLyrics(HyLyricInfo lyricInfo)
    {
        if (!ReferenceEquals(state.LyricInfo, lyricInfo))
        {
            state.LyricInfo = lyricInfo;
            state.LyricIndex = 0;
        }

        state.IsLyricPending = false;
    }

    private void PublishPendingLyrics()
    {
        state.IsLyricPending = true;
        state.LyricInfo = new HyLyricInfo();
        state.LyricIndex = 0;
    }

    private async Task ResolveLyricsAsync(SingleSongBase providerItem, LyricLoad load, CancellationToken ct)
    {
        var cacheId = providerItem.ActualId;
        var canUseHyLyricInfoCache = providerItem is not NeteaseRadioProgram && !string.IsNullOrWhiteSpace(cacheId);
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
                    if (HasKaraokePreferenceMismatch(cached) || HasUnusedWordTranslation(cached))
                    {
                        var converted = await ConvertPureLyricInfoAsync(cached.PureLyricInfo,
                            GetArtistText(providerItem));
                        cached = converted;
                        taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                                CacheType.HyLyricInfo, cacheId,
                                () => Task.FromResult(converted),
                                forceRefresh: true,
                                cancellationToken: ct),
                            "refresh lyric cache after karaoke preference change");
                    }

                    load.Report(cached);
                    if (cached.PureLyricInfo is not HyALRCLyricInfo)
                        await TryLoadAmllTtmlAsync(providerItem, load, ct);
                    return;
                }

                forceRefreshHyLyricInfoCache = true;
            }
        }

        var pureLyricInfo = providerItem switch
        {
            LocalSong localSong => await LoadLocalLyricAsync(localSong),
            NeteaseRadioProgram => new PureLyricInfo(),
            _ => await LoadProviderLyricAsync(providerItem, ct, load)
        };
        var lyricInfo = await ConvertPureLyricInfoAsync(pureLyricInfo, GetArtistText(providerItem));

        load.Report(lyricInfo);

        if (canUseHyLyricInfoCache && HasCacheableLyrics(lyricInfo, providerItem))
            taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo, cacheId,
                    () => Task.FromResult(lyricInfo),
                    forceRefresh: forceRefreshHyLyricInfoCache,
                    cancellationToken: ct),
                "cache provider lyric info");

        await TryLoadAmllTtmlAsync(providerItem, load, ct);
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
        lock (_loadSync)
        {
            if (currentSong is not null && _currentLoad is { } current && current.Matches(currentSong))
                current.Override(lyricInfo);
        }

        state.LyricInfo = lyricInfo;
        state.LyricIndex = 0;
        state.IsLyricPending = false;

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

    public async Task<IReadOnlyList<(double Start, double End)>> GetVocalRegionsAsync(SingleSongBase providerItem,
        CancellationToken ct = default)
    {
        List<SongLyric>? lines = null;
        if (ReferenceEquals(state.NowPlayingProviderItem, providerItem) && state.LyricInfo?.Lyrics is { Count: > 0 } current)
            lines = current;
        if (lines is null && providerItem is not NeteaseRadioProgram &&
            !string.IsNullOrWhiteSpace(providerItem.ActualId))
        {
            var cached = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, providerItem.ActualId,
                () => Task.FromResult<HyLyricInfo>(null),
                cancellationToken: ct);
            if (cached?.Lyrics is { Count: > 0 }) lines = cached.Lyrics;
        }

        if (lines is null)
        {
            var pure = providerItem switch
            {
                LocalSong localSong => await LoadLocalLyricAsync(localSong),
                NeteaseRadioProgram => new PureLyricInfo(),
                _ => await LoadProviderLyricAsync(providerItem, ct)
            };
            lines = pure is KaraokLyricInfo { KaraokLyric.Length: > 0 }
                ? Utils.ConvertKaraok(pure, setting.MigrateLyrics)
                : Utils.ConvertPureLyric(pure.PureLyrics);
        }

        var first = 0;
        var last = lines.Count - 1;
        while (first <= last && IsCreditLine(lines[first].LyricLine?.CurrentLyric)) first++;
        while (last >= first && IsCreditLine(lines[last].LyricLine?.CurrentLyric)) last--;

        var regions = new List<(double Start, double End)>();
        for (var i = first; i <= last; i++)
        {
            var line = lines[i].LyricLine;
            if (line is null || string.IsNullOrWhiteSpace(line.CurrentLyric)) continue;
            var start = line.StartTime.TotalSeconds;
            double end;
            if (line is KaraokeLyricsLine { WordInfos: { Count: > 0 } words })
            {
                end = (words[^1].StartTime + words[^1].Duration).TotalSeconds;
            }
            else
            {
                var length = i < last ? 8 : EstimateLineSeconds(line.CurrentLyric);
                var next = i + 1 < lines.Count ? lines[i + 1].LyricLine.StartTime.TotalSeconds : start + length;
                end = Math.Min(next, start + length);
            }

            if (end > start) regions.Add((start, end));
        }

        if (regions.Count < 3) regions.Clear();
        return regions;
    }

    [GeneratedRegex(@"^\s*[^:：]{1,24}\s*[:：]\s*\S")]
    private static partial Regex CreditLineRegex();

    private static bool IsCreditLine(string? text)
    {
        return string.IsNullOrWhiteSpace(text) || CreditLineRegex().IsMatch(text) || text.Contains("纯音乐");
    }

    private static double EstimateLineSeconds(string text)
    {
        var units = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (c >= 0x2E80)
            {
                units++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                if (!inWord) units++;
                inWord = true;
            }
            else
            {
                inWord = false;
            }
        }

        return Math.Clamp(1.5 + 0.4 * units, 3, 8);
    }

    #region Private helpers

    private async Task<PureLyricInfo> LoadProviderLyricAsync(SingleSongBase providerItem, CancellationToken ct,
        LyricLoad? load = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(providerItem.ActualId))
                return new PureLyricInfo { PureLyrics = "[00:00.000] 无歌词 请欣赏" };

            var lyricResult = await lyricProvider.GetLyricInfoAsync(providerItem, ct);

            if (lyricResult is null)
            {
                load?.MarkDegraded();
                return new PureLyricInfo { PureLyrics = "[00:00.000] 歌词获取失败" };
            }

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
            load?.MarkDegraded();
            return new PureLyricInfo();
        }
    }

    private async Task<HyLyricInfo> ConvertPureLyricInfoAsync(PureLyricInfo pureLyricInfo, string artistText)
    {
        var lyricInfo = new HyLyricInfo();
        var sourceLyricInfo = pureLyricInfo;
        if (!setting.KaraokeLyric && pureLyricInfo is KaraokLyricInfo wordLyricInfo &&
            !string.IsNullOrWhiteSpace(pureLyricInfo.PureLyrics))
            pureLyricInfo = new PureLyricInfo
            {
                PureLyrics = pureLyricInfo.PureLyrics,
                TrLyrics = string.IsNullOrWhiteSpace(pureLyricInfo.TrLyrics)
                    ? wordLyricInfo.YrTrLyrics
                    : pureLyricInfo.TrLyrics,
                NeteaseRomaji = pureLyricInfo.NeteaseRomaji,
                SongMetadata = pureLyricInfo.SongMetadata,
                LyricMetadata = pureLyricInfo.LyricMetadata
            };
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
        lyricInfo.PureLyricInfo = sourceLyricInfo;
        return lyricInfo;
    }

    private bool HasKaraokePreferenceMismatch(HyLyricInfo lyricInfo)
    {
        return lyricInfo.PureLyricInfo is KaraokLyricInfo karaoke &&
               !string.IsNullOrWhiteSpace(karaoke.KaraokLyric) &&
               !string.IsNullOrWhiteSpace(karaoke.PureLyrics) &&
               lyricInfo.Lyrics.Any(t => t.LyricLine is KaraokeLyricsLine) != setting.KaraokeLyric;
    }

    private bool HasUnusedWordTranslation(HyLyricInfo lyricInfo)
    {
        return !setting.KaraokeLyric &&
               lyricInfo.PureLyricInfo is KaraokLyricInfo karaoke &&
               !string.IsNullOrWhiteSpace(karaoke.PureLyrics) &&
               string.IsNullOrWhiteSpace(karaoke.TrLyrics) &&
               !string.IsNullOrWhiteSpace(karaoke.YrTrLyrics) &&
               !lyricInfo.Lyrics.Any(t => t.HaveTranslation);
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
        var path = item.StorageFile?.Path ?? item.ActualId;
        if (string.IsNullOrWhiteSpace(path))
            return new PureLyricInfo();

        try
        {
            var lrcPath = Path.ChangeExtension(path, "lrc");
            var file = await StorageFile.GetFileFromPathAsync(lrcPath);
            var text = await FileIO.ReadTextAsync(file);
            if (!string.IsNullOrWhiteSpace(text))
                return new PureLyricInfo { PureLyrics = text };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
        }

        if (item.IsNcm)
            return new PureLyricInfo();

        try
        {
            var embedded = await Task.Run(() => ReadEmbeddedLyricAsync(item, path));
            if (!string.IsNullOrWhiteSpace(embedded))
                return new PureLyricInfo { PureLyrics = embedded };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
        }

        return new PureLyricInfo();
    }

    private static async Task<string?> ReadEmbeddedLyricAsync(LocalSong item, string path)
    {
        var file = item.StorageFile ?? await StorageFile.GetFileFromPathAsync(path);
        using var abstraction = await UwpStorageFileAbstraction.OpenAsync(file);
        using var tagFile = TagLibHelper.Create(abstraction, file.FileType.ToLowerInvariant());
        if (tagFile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
            foreach (var frame in id3.GetFrames<TagLib.Id3v2.SynchronisedLyricsFrame>())
            {
                if (frame.Format != TagLib.Id3v2.TimestampFormat.AbsoluteMilliseconds || frame.Text.Length == 0)
                    continue;

                var builder = new StringBuilder();
                foreach (var line in frame.Text)
                {
                    var time = TimeSpan.FromMilliseconds(line.Time);
                    builder.Append(
                        $"[{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}]{line.Text?.Trim()}\n");
                }

                return builder.ToString();
            }

        var lyrics = tagFile.Tag.Lyrics;
        if (string.IsNullOrWhiteSpace(lyrics)) return null;

        lyrics = lyrics.Replace("\r\n", "\n").Replace('\r', '\n');
        if (LrcTimestampRegex().IsMatch(lyrics)) return lyrics;

        var plain = new StringBuilder();
        foreach (var line in lyrics.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            plain.Append("[00:00.00]").Append(line.Trim()).Append('\n');
        }

        return plain.ToString();
    }

    [GeneratedRegex(@"\[\d{1,3}:\d{1,2}([.:]\d{1,3})?\]")]
    private static partial Regex LrcTimestampRegex();

    private static readonly string[] AmllFallbackMirrors =
    [
        LyricSettings.DefaultAmllTtmlMirrorUrl,
        "https://gcore.jsdelivr.net/gh/amll-dev/amll-ttml-db@main/ncm-lyrics/[NCM_ID].ttml",
        "https://amll-ttml-db.stevexmh.net/ncm/[NCM_ID]"
    ];

    private readonly HashSet<string> _amllMissingIds = [];

    private async Task<string?> FetchAmllTtmlAsync(string id, CancellationToken ct)
    {
        var mirrors = new List<string> { setting.AmllTtmlMirrorUrl };
        foreach (var mirror in AmllFallbackMirrors)
            if (!mirrors.Contains(mirror))
                mirrors.Add(mirror);

        var allMissing = true;
        foreach (var mirror in mirrors)
        {
            if (string.IsNullOrWhiteSpace(mirror) || !mirror.Contains("[NCM_ID]")) continue;
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, mirror.Replace("[NCM_ID]", id));
                message.Headers.Add("User-Agent", "HyPlayer LyricsClient");
                using var response = await httpClient.SendAsync(message, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) continue;
                allMissing = false;
                if (!response.IsSuccessStatusCode) continue;
                var content = await response.Content.ReadAsStringAsync(ct);
                if (content.AsSpan().TrimStart().StartsWith("<tt", StringComparison.Ordinal)) return content;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                allMissing = false;
            }
        }

        if (allMissing)
            lock (_amllMissingIds)
            {
                _amllMissingIds.Add(id);
            }

        return null;
    }

    private async Task TryLoadAmllTtmlAsync(SingleSongBase item, LyricLoad load, CancellationToken ct)
    {
        try
        {
            if (!setting.EnableAmllTtmlDb || item is not NeteaseSong || string.IsNullOrWhiteSpace(item.ActualId)) return;
            lock (_amllMissingIds)
            {
                if (_amllMissingIds.Contains(item.ActualId)) return;
            }

            var ttmlContent = await FetchAmllTtmlAsync(item.ActualId, ct);
            if (ttmlContent is null) return;
            var importedLyric = await ConvertAlrcLyricAsync(
                ttmlContent,
                ".ttml",
                "amll-ttml-db",
                $"https://github.com/amll-dev/amll-ttml-db/blob/main/ncm-lyrics/{item.ActualId}.ttml",
                true);
            ct.ThrowIfCancellationRequested();
            load.Report(importedLyric);

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
