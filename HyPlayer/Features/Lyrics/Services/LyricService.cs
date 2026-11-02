using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using HyPlayer.Features.Lyrics.Services.ThirdParty;
using HyPlayer.Features.Playback.Services;
using HyPlayer.NeteaseProvider.LocalMusic;
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
    IBackgroundTaskRunner taskRunner,
    IKawazuStateService kawazuState,
    ThirdPartyLyricSources thirdPartyLyrics,
    LyricSourcePreferences sourcePreferences) : ILyricService
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

    private const string NoLyricText = "无歌词 请欣赏";
    private const string NeteaseNoLyricText = "暂无歌词";
    private const string LrclibSourceName = "lrclib";
    private const string AmllSourceName = "amll-ttml-db";
    private const string LyricFailedText = "歌词获取失败";

    private enum CachedLyricState
    {
        Reported,
        Missing,
        Stale
    }

    private sealed record ProviderLyric(PureLyricInfo Info, bool Missing);

    private async Task ResolveLyricsAsync(SingleSongBase providerItem, LyricLoad load, CancellationToken ct)
    {
        if (providerItem is LocalSong localSong)
        {
            await ResolveLocalLyricsAsync(localSong, load, ct);
            return;
        }

        var cacheId = providerItem.ActualId;
        var canUseHyLyricInfoCache = providerItem is not NeteaseRadioProgram && !string.IsNullOrWhiteSpace(cacheId);
        var forceRefreshHyLyricInfoCache = false;
        if (canUseHyLyricInfoCache)
        {
            var cachedState = await TryReportCachedLyricsAsync(providerItem, load, ct);
            if (cachedState == CachedLyricState.Reported) return;
            forceRefreshHyLyricInfoCache = cachedState == CachedLyricState.Stale;
        }

        var providerLyric = await LoadProviderLyricAsync(providerItem, ct, load);
        var lyricInfo = await ConvertPureLyricInfoAsync(providerLyric.Info, GetArtistText(providerItem));

        load.Report(lyricInfo);

        if (canUseHyLyricInfoCache && HasCacheableLyrics(lyricInfo, providerItem))
            taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo, cacheId,
                    () => Task.FromResult(lyricInfo),
                    forceRefresh: forceRefreshHyLyricInfoCache,
                    cancellationToken: ct),
                "cache provider lyric info");

        var own = providerLyric.Missing ? null : lyricInfo;
        RememberOwnLyric(providerItem, own);
        await ApplyLyricSourcesAsync(providerItem, own, load, ct);
    }

    private async Task ResolveLocalLyricsAsync(LocalSong localSong, LyricLoad load, CancellationToken ct)
    {
        var localLyric = await ConvertPureLyricInfoAsync(await LoadLocalLyricAsync(localSong), GetArtistText(localSong));
        if (HasDisplayableLyrics(localLyric, localSong))
        {
            load.Report(localLyric);
            RememberOwnLyric(localSong, localLyric);
            await ApplyLyricSourcesAsync(localSong, localLyric, load, ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(localSong.ActualId) &&
            await TryReportCachedLyricsAsync(localSong, load, ct) == CachedLyricState.Reported)
            return;

        load.Report(localLyric);
        await ApplyLyricSourcesAsync(localSong, null, load, ct);
    }

    private async Task<CachedLyricState> TryReportCachedLyricsAsync(SingleSongBase providerItem, LyricLoad load,
        CancellationToken ct)
    {
        var cacheId = providerItem.ActualId;
        var cached = await SimpleCacher.GetOrCreateCacheAsync(
            CacheType.HyLyricInfo, cacheId,
            () => Task.FromResult<HyLyricInfo>(null),
            cancellationToken: ct);

        if (cached is null) return CachedLyricState.Missing;
        var choice = sourcePreferences.Get(providerItem);
        if (!HasCacheableLyrics(cached, providerItem) || HasLegacyNeteaseSourceMetadata(cached) ||
            (IsLyricSource(cached, AmllSourceName) && cached.PureLyricInfo is not HyALRCLyricInfo) ||
            (choice == LyricSourceKind.Own && IsThirdPartyOrImportedLyric(cached)))
            return CachedLyricState.Stale;

        if (HasKaraokePreferenceMismatch(cached) || HasUnusedWordTranslation(cached))
        {
            var converted = await ConvertPureLyricInfoAsync(cached.PureLyricInfo, GetArtistText(providerItem));
            cached = converted;
            taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.HyLyricInfo, cacheId,
                    () => Task.FromResult(converted),
                    forceRefresh: true,
                    cancellationToken: ct),
                "refresh lyric cache after karaoke preference change");
        }

        load.Report(cached);
        var cachedIsOwn = !IsThirdPartyOrImportedLyric(cached);
        if (cachedIsOwn) RememberOwnLyric(providerItem, cached);
        if (choice != LyricSourceKind.Auto)
        {
            if (SourceOf(cached) != choice)
                await ApplyLyricSourcesAsync(providerItem, cachedIsOwn ? cached : null, load, ct);
        }
        else if (IsLyricSource(cached, LrclibSourceName))
        {
            await TryLoadThirdPartyLyricsAsync(providerItem, null, load, ct);
        }
        else if (cachedIsOwn)
        {
            await TryLoadThirdPartyLyricsAsync(providerItem, cached, load, ct);
        }

        return CachedLyricState.Reported;
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
                _ => (await LoadProviderLyricAsync(providerItem, ct)).Info
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

    private async Task<ProviderLyric> LoadProviderLyricAsync(SingleSongBase providerItem, CancellationToken ct,
        LyricLoad? load = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(providerItem.ActualId))
                return MissingProviderLyric(providerItem);

            var lyricResult = await lyricProvider.GetLyricInfoAsync(providerItem, ct);

            if (lyricResult is null)
            {
                load?.MarkDegraded();
                return new ProviderLyric(new PureLyricInfo { PureLyrics = $"[00:00.000] {LyricFailedText}" }, false);
            }

            if (lyricResult.Count == 0)
                return MissingProviderLyric(providerItem);

            return new ProviderLyric(await ConvertProviderLyricsAsync(lyricResult, ct), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyric error: {ex.Message}");
            load?.MarkDegraded();
            return new ProviderLyric(new PureLyricInfo(), false);
        }
    }

    private static ProviderLyric MissingProviderLyric(SingleSongBase providerItem)
    {
        return new ProviderLyric(
            providerItem is NeteaseRadioProgram
                ? new PureLyricInfo()
                : new PureLyricInfo { PureLyrics = $"[00:00.000] {NoLyricText}" },
            true);
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

    private sealed record LyricLookup(string? NeteaseId, LyricSearchQuery? Query);

    private enum OwnLyricKind
    {
        Missing,
        Line,
        Word
    }

    private sealed record ThirdPartyCandidate(
        PureLyricInfo? Info,
        HyLyricInfo? Converted,
        bool CheckDuration,
        bool Failed)
    {
        public static ThirdPartyCandidate None { get; } = new(null, null, false, false);
    }

    private OwnLyricKind ClassifyOwnLyric(HyLyricInfo? own, SingleSongBase item)
    {
        if (own is null || !HasDisplayableLyrics(own, item) || IsNoLyricPlaceholder(own) ||
            CountEffectiveLines(own) <= (int)setting.AutoHideLyricPanelLines)
            return OwnLyricKind.Missing;
        return LyricWordTiming.IsWordLevel(own.PureLyricInfo) ? OwnLyricKind.Word : OwnLyricKind.Line;
    }

    internal static int CountEffectiveLines(HyLyricInfo lyricInfo)
    {
        return lyricInfo.Lyrics.Count(t => !string.IsNullOrWhiteSpace(t.LyricLine?.CurrentLyric));
    }

    private async Task TryLoadThirdPartyLyricsAsync(SingleSongBase item, HyLyricInfo? own, LyricLoad load,
        CancellationToken ct)
    {
        if (!thirdPartyLyrics.AllowsSong(item)) return;
        var kind = ClassifyOwnLyric(own, item);
        var canSearch = thirdPartyLyrics.CanSearch(item);
        if (!thirdPartyLyrics.AmllEnabled && !(kind != OwnLyricKind.Word && canSearch)) return;

        var lookup = await BuildLyricLookupAsync(item);
        var query = lookup.Query;
        var amllNameSearch = canSearch &&
                             (kind != OwnLyricKind.Word || ThirdPartyLyricSources.SearchesWithOwnLyrics(item));
        var searchOthers = canSearch && query is not null && kind != OwnLyricKind.Word;

        var amllTask = FetchAmllAsync(item, lookup, amllNameSearch, false, ct);
        var kugouTask = searchOthers && thirdPartyLyrics.KugouEnabled
            ? FetchKugouAsync(query!, ct)
            : Task.FromResult(ThirdPartyCandidate.None);
        var lrclibTask = searchOthers && kind == OwnLyricKind.Missing && thirdPartyLyrics.LrclibEnabled
            ? FetchLrclibAsync(query!, ct)
            : Task.FromResult(ThirdPartyCandidate.None);
        var candidates = await Task.WhenAll(amllTask, kugouTask, lrclibTask);

        HyLyricInfo? fallback = null;
        foreach (var candidate in candidates)
        {
            if (await AcceptCandidateAsync(item, candidate, query) is not { } converted) continue;
            if (LyricWordTiming.IsWordLevel(converted.PureLyricInfo))
            {
                PublishThirdPartyLyrics(item, converted, load, ct);
                return;
            }

            fallback ??= converted;
        }

        if (kind == OwnLyricKind.Missing && fallback is not null)
        {
            PublishThirdPartyLyrics(item, fallback, load, ct);
            return;
        }

        if (canSearch && !candidates.Any(c => c.Failed)) thirdPartyLyrics.MarkMissing(item);
    }

    private async Task<ThirdPartyCandidate> FetchAmllAsync(SingleSongBase item, LyricLookup lookup, bool nameSearch,
        bool manual, CancellationToken ct)
    {
        if (thirdPartyLyrics.CanFetchAmllById(item, lookup.NeteaseId, manual) &&
            await thirdPartyLyrics.FetchAmllByIdAsync(lookup.NeteaseId!, ct) is { } ttml &&
            await ConvertAmllAsync(ttml, AmllLyricClient.NcmLyricUri(lookup.NeteaseId!)) is { } byId)
            return new ThirdPartyCandidate(null, byId, false, false);

        if (!nameSearch || !thirdPartyLyrics.AmllEnabled || lookup.Query is null) return ThirdPartyCandidate.None;
        var found = await thirdPartyLyrics.SearchAmllAsync(lookup.Query, ct);
        return found.Lyric is { } lyric && await ConvertAmllAsync(lyric.Text, lyric.SourceUri) is { } info
            ? new ThirdPartyCandidate(null, info, true, false)
            : new ThirdPartyCandidate(null, null, false, found.Failed);
    }

    private async Task<HyLyricInfo?> AcceptCandidateAsync(SingleSongBase item, ThirdPartyCandidate candidate,
        LyricSearchQuery? query)
    {
        var converted = candidate.Converted ??
                        (candidate.Info is null
                            ? null
                            : await ConvertPureLyricInfoAsync(candidate.Info, GetArtistText(item)));
        if (converted is null || !HasDisplayableLyrics(converted, item)) return null;
        if (candidate.CheckDuration && query is not null && !ThirdPartyLyricMatcher.FitsDuration(query, converted))
            return null;
        return converted;
    }

    private readonly object _sourceSync = new();
    private (string Key, HyLyricInfo Info)? _ownLyric;
    private string? _probeKey;
    private Dictionary<LyricSourceKind, HyLyricInfo?> _probeResults = [];

    private static readonly LyricSourceKind[] SelectableSources =
        [LyricSourceKind.Own, LyricSourceKind.Amll, LyricSourceKind.Kugou, LyricSourceKind.Lrclib];

    private static string SourceKey(SingleSongBase item)
    {
        return $"{item.ProviderId}:{item.TypeId}:{item.ActualId}";
    }

    private void RememberOwnLyric(SingleSongBase item, HyLyricInfo? own)
    {
        if (own is null || !HasDisplayableLyrics(own, item) || IsNoLyricPlaceholder(own)) return;
        lock (_sourceSync)
        {
            _ownLyric = (SourceKey(item), own);
        }
    }

    private bool IsSourceEnabled(LyricSourceKind kind)
    {
        return kind switch
        {
            LyricSourceKind.Amll => thirdPartyLyrics.AmllEnabled,
            LyricSourceKind.Kugou => thirdPartyLyrics.KugouEnabled,
            LyricSourceKind.Lrclib => thirdPartyLyrics.LrclibEnabled,
            _ => true
        };
    }

    private static LyricSourceKind? SourceOf(HyLyricInfo lyricInfo)
    {
        if (IsLyricSource(lyricInfo, AmllSourceName)) return LyricSourceKind.Amll;
        if (IsLyricSource(lyricInfo, KugouLyricClient.SourceName)) return LyricSourceKind.Kugou;
        if (IsLyricSource(lyricInfo, LrclibSourceName)) return LyricSourceKind.Lrclib;
        return lyricInfo.PureLyricInfo is HyALRCLyricInfo ? null : LyricSourceKind.Own;
    }

    private async Task ApplyLyricSourcesAsync(SingleSongBase item, HyLyricInfo? own, LyricLoad load,
        CancellationToken ct)
    {
        var choice = sourcePreferences.Get(item);
        if (choice == LyricSourceKind.Own) return;
        if (choice != LyricSourceKind.Auto && await FetchSourceAsync(item, choice, ct) is { } chosen)
        {
            PublishThirdPartyLyrics(item, chosen, load, ct);
            return;
        }

        await TryLoadThirdPartyLyricsAsync(item, own, load, ct);
    }

    private async Task<HyLyricInfo?> FetchSourceAsync(SingleSongBase item, LyricSourceKind kind,
        CancellationToken ct)
    {
        if (!IsSourceEnabled(kind)) return null;
        var lookup = await BuildLyricLookupAsync(item);
        return await AcceptCandidateAsync(item, await FetchCandidateAsync(item, kind, lookup, ct), lookup.Query);
    }

    private Task<ThirdPartyCandidate> FetchCandidateAsync(SingleSongBase item, LyricSourceKind kind,
        LyricLookup lookup, CancellationToken ct)
    {
        if (!IsSourceEnabled(kind)) return Task.FromResult(ThirdPartyCandidate.None);
        return kind switch
        {
            LyricSourceKind.Amll => FetchAmllAsync(item, lookup, true, true, ct),
            LyricSourceKind.Kugou when lookup.Query is { } query => FetchKugouAsync(query, ct),
            LyricSourceKind.Lrclib when lookup.Query is { } query => FetchLrclibAsync(query, ct),
            _ => Task.FromResult(ThirdPartyCandidate.None)
        };
    }

    private async Task<HyLyricInfo?> LoadOwnLyricAsync(SingleSongBase item, CancellationToken ct)
    {
        lock (_sourceSync)
        {
            if (_ownLyric is { } remembered && remembered.Key == SourceKey(item)) return remembered.Info;
        }

        HyLyricInfo converted;
        if (item is LocalSong localSong)
        {
            converted = await ConvertPureLyricInfoAsync(await LoadLocalLyricAsync(localSong), GetArtistText(item));
        }
        else
        {
            var provider = await LoadProviderLyricAsync(item, ct);
            if (provider.Missing) return null;
            converted = await ConvertPureLyricInfoAsync(provider.Info, GetArtistText(item));
        }

        if (!HasDisplayableLyrics(converted, item) || IsNoLyricPlaceholder(converted)) return null;
        RememberOwnLyric(item, converted);
        return converted;
    }

    public LyricSourceKind GetLyricSourceChoice(SingleSongBase song)
    {
        return sourcePreferences.Get(song);
    }

    public LyricSourceKind? CurrentLyricSource => SourceOf(state.LyricInfo);

    public async Task<IReadOnlyList<LyricSourceOption>> GetLyricSourceOptionsAsync(SingleSongBase song,
        CancellationToken ct = default)
    {
        var key = SourceKey(song);
        Dictionary<LyricSourceKind, HyLyricInfo?> known;
        lock (_sourceSync)
        {
            known = _probeKey == key ? new Dictionary<LyricSourceKind, HyLyricInfo?>(_probeResults) : [];
        }

        var missing = SelectableSources
            .Where(kind => kind != LyricSourceKind.Own && IsSourceEnabled(kind) && !known.ContainsKey(kind))
            .ToList();
        if (missing.Count > 0)
        {
            var lookup = await BuildLyricLookupAsync(song);
            var candidates = await Task.WhenAll(missing.Select(kind => FetchCandidateAsync(song, kind, lookup, ct)));
            for (var i = 0; i < missing.Count; i++)
                known[missing[i]] = await AcceptCandidateAsync(song, candidates[i], lookup.Query);
        }

        if (!known.ContainsKey(LyricSourceKind.Own)) known[LyricSourceKind.Own] = await LoadOwnLyricAsync(song, ct);

        lock (_sourceSync)
        {
            _probeKey = key;
            _probeResults = known;
        }

        return SelectableSources.Select(kind =>
        {
            if (!IsSourceEnabled(kind)) return new LyricSourceOption(kind, LyricSourceState.Disabled, false);
            return known.TryGetValue(kind, out var info) && info is not null
                ? new LyricSourceOption(kind, LyricSourceState.Available, LyricWordTiming.IsWordLevel(info.PureLyricInfo))
                : new LyricSourceOption(kind, LyricSourceState.Unavailable, false);
        }).ToList();
    }

    public async Task SelectLyricSourceAsync(SingleSongBase song, LyricSourceKind kind, CancellationToken ct = default)
    {
        sourcePreferences.Set(song, kind);
        if (kind == LyricSourceKind.Auto)
        {
            if (!string.IsNullOrWhiteSpace(song.ActualId))
                await SimpleCacher.ResetCacheAsync(CacheType.HyLyricInfo, song.ActualId);
            lock (_loadSync)
            {
                if (_currentLoad is { } current && current.Matches(song))
                {
                    current.Cancel();
                    _currentLoad = null;
                }
            }

            await LoadLyricsAsync(song, ct);
            return;
        }

        HyLyricInfo? info;
        lock (_sourceSync)
        {
            info = _probeKey == SourceKey(song) && _probeResults.TryGetValue(kind, out var known) ? known : null;
        }

        info ??= kind == LyricSourceKind.Own
            ? await LoadOwnLyricAsync(song, ct)
            : await FetchSourceAsync(song, kind, ct);
        if (info is null) return;

        lock (_loadSync)
        {
            if (_currentLoad is { } current && current.Matches(song)) current.Override(info);
        }

        state.LyricInfo = info;
        state.LyricIndex = 0;
        state.IsLyricPending = false;
        if (song is NeteaseRadioProgram || string.IsNullOrWhiteSpace(song.ActualId) || !HasCacheableLyrics(info, song))
            return;
        taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, song.ActualId,
                () => Task.FromResult(info),
                forceRefresh: true,
                cancellationToken: ct),
            "cache selected lyric source");
    }

    private async Task<ThirdPartyCandidate> FetchKugouAsync(LyricSearchQuery query, CancellationToken ct)
    {
        var found = await thirdPartyLyrics.SearchKugouAsync(query, ct);
        return new ThirdPartyCandidate(found.Lyric, null, true, found.Failed);
    }

    private async Task<ThirdPartyCandidate> FetchLrclibAsync(LyricSearchQuery query, CancellationToken ct)
    {
        var found = await thirdPartyLyrics.GetLrclibAsync(query, ct);
        if (found.Lyric is not { } lyric) return new ThirdPartyCandidate(null, null, false, found.Failed);
        return new ThirdPartyCandidate(new PureLyricInfo
        {
            PureLyrics = lyric.Text,
            LyricMetadata =
            [
                new LyricInfoMetadata
                {
                    Key = "source",
                    Value = LrclibSourceName,
                    DisplayName = "歌词来源",
                    ActionUri = lyric.SourceUri
                }
            ]
        }, null, false, false);
    }

    private static async Task<HyLyricInfo?> ConvertAmllAsync(string ttml, string? sourceUri)
    {
        try
        {
            return await ConvertAlrcLyricAsync(ttml, ".ttml", AmllSourceName, sourceUri, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"AMLL lyric conversion failed: {ex.Message}");
            return null;
        }
    }

    private void PublishThirdPartyLyrics(SingleSongBase item, HyLyricInfo lyricInfo, LyricLoad load,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        load.Report(lyricInfo);
        if (item is NeteaseRadioProgram || string.IsNullOrWhiteSpace(item.ActualId) ||
            !HasCacheableLyrics(lyricInfo, item))
            return;
        taskRunner.Forget(SimpleCacher.GetOrCreateCacheAsync(
                CacheType.HyLyricInfo, item.ActualId,
                () => Task.FromResult(lyricInfo),
                forceRefresh: true,
                cancellationToken: ct),
            "cache third-party lyric info");
    }

    private static async Task<LyricLookup> BuildLyricLookupAsync(SingleSongBase item)
    {
        switch (item)
        {
            case NeteaseRadioProgram program:
                return new LyricLookup(program.MainSong?.ActualId,
                    LyricSearchQueryBuilder.FromProgram(program.Name, program.Description, program.Duration));
            case LocalSong localSong:
                return await BuildLocalLookupAsync(localSong);
            case NeteaseSong or NeteaseCloudLibraryItem:
                return new LyricLookup(item.ActualId,
                    LyricSearchQueryBuilder.FromSong(item.Name, item.CreatorList, item.Duration));
            default:
                return new LyricLookup(null,
                    LyricSearchQueryBuilder.FromSong(item.Name, item.CreatorList, item.Duration));
        }
    }

    private static async Task<LyricLookup> BuildLocalLookupAsync(LocalSong item)
    {
        var query = LyricSearchQueryBuilder.FromSong(item.Name, item.CreatorList, item.Duration);
        if (item.IsNcm || (item.NeteaseId is not null && query is { DurationMs: > 0 }))
            return new LyricLookup(item.NeteaseId, query);

        var tags = await ReadLocalLookupAsync(item);
        return new LyricLookup(item.NeteaseId ?? tags?.NeteaseId, tags?.Query ?? query);
    }

    private static async Task<LyricLookup?> ReadLocalLookupAsync(LocalSong item)
    {
        var path = item.StorageFile?.Path ?? item.ActualId;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var file = item.StorageFile ?? await StorageFile.GetFileFromPathAsync(path);
            using var abstraction = await UwpStorageFileAbstraction.OpenAsync(file);
            using var tagFile = TagLibHelper.Create(abstraction, file.FileType.ToLowerInvariant());
            var neteaseId = The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var key) && key.MusicId > 0
                ? key.MusicId.ToString()
                : null;
            var title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                ? Path.GetFileNameWithoutExtension(file.Name)
                : tagFile.Tag.Title;
            return new LyricLookup(neteaseId, LyricSearchQueryBuilder.FromSong(title, tagFile.Tag.Performers,
                (long)tagFile.Properties.Duration.TotalMilliseconds));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Local tag read for lyric lookup failed: {ex.Message}");
            return null;
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
        return HasDisplayableLyrics(lyricInfo, providerItem) && !IsNoLyricPlaceholder(lyricInfo);
    }

    private static bool IsThirdPartyOrImportedLyric(HyLyricInfo lyricInfo)
    {
        return lyricInfo.PureLyricInfo is HyALRCLyricInfo ||
               IsLyricSource(lyricInfo, LrclibSourceName) ||
               IsLyricSource(lyricInfo, KugouLyricClient.SourceName) ||
               IsLyricSource(lyricInfo, AmllSourceName);
    }

    private static bool IsLyricSource(HyLyricInfo lyricInfo, string source)
    {
        return lyricInfo.LyricMetadata.Any(t =>
            string.Equals(t.Key, "source", StringComparison.Ordinal) &&
            string.Equals(t.Value, source, StringComparison.Ordinal));
    }

    internal static bool IsNoLyricPlaceholder(HyLyricInfo lyricInfo)
    {
        var lines = lyricInfo.Lyrics
            .Where(t => !string.IsNullOrWhiteSpace(t.LyricLine.CurrentLyric))
            .Take(2)
            .ToList();
        return lines.Count == 1 &&
               lines[0].LyricLine.CurrentLyric.Trim() is NoLyricText or NeteaseNoLyricText or LyricFailedText;
    }

    private static bool HasLegacyNeteaseSourceMetadata(HyLyricInfo lyricInfo)
    {
        return lyricInfo.LyricMetadata.Any(t =>
            string.Equals(t.Key, "source", StringComparison.Ordinal) &&
            t.Value?.StartsWith("netease:", StringComparison.OrdinalIgnoreCase) is true);
    }

    #endregion
}
