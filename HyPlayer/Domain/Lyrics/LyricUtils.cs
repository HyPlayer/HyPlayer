using ALRC.Abstraction;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Lyrics.LyricParser.Implementation;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using Kawazu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Domain.Lyrics;

public static class Utils
{
    public static List<SongLyric> ConvertPureLyric(string lyricAllText)
    {
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        return [.. parsedlyrics.Lines.OrderBy(t => t.StartTime).Select(lyricsLine => new SongLyric
        { LyricLine = lyricsLine, Translation = null })];
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime))
            {
                songLyric.Translation = lyricsLine.CurrentLyric;
                break;
            }
    }

    public static void ConvertYrcTranslation(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics)
    {
        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrTrLyrics.AsSpan());
        if (Ioc.Default.GetRequiredService<Setting>().MigrateLyrics)
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.TrLyrics.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }

    public static void ConvertNeteaseRomaji(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricAllText)) return;
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime ||
                         songLyric.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
            {
                songLyric.Romaji = lyricsLine.CurrentLyric;
                break;
            }
    }

    public static void ConvertYrcNeteaseRomaji(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricInfo.NeteaseRomaji) && string.IsNullOrEmpty(lyricInfo.YrNeteaseRomaji)) return;
        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrNeteaseRomaji.AsSpan());
        if (Ioc.Default.GetRequiredService<Setting>().MigrateLyrics)
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.NeteaseRomaji.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                             t.LyricLine.StartTime == lyricsLine.StartTime ||
                             t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }

    public static async Task ConvertKawazuRomaji(List<SongLyric> lyrics)
    {
        var kawazu = Ioc.Default.GetRequiredService<IKawazuStateService>().Converter;
        if (kawazu is null) return;
        foreach (var lyricItem in lyrics)
        {
            if (!string.IsNullOrWhiteSpace(lyricItem.LyricLine.CurrentLyric))
            {
                if (!Utilities.HasKana(lyricItem.LyricLine.CurrentLyric)) continue;
                lyricItem.Romaji =
                    await kawazu.Convert(lyricItem.LyricLine.CurrentLyric, To.Romaji, Mode.Separated);
                if (lyricItem.LyricLine is not KaraokeLyricsLine klyric) continue;
                var list = await kawazu.GetDivisions(lyricItem.LyricLine.CurrentLyric, To.Romaji,
                    Mode.Separated, RomajiSystem.Hepburn, "", "");
                SetRomajiKaraoke(list, [.. klyric.WordInfos]);
            }
        }
    }

    public static void SetRomajiKaraoke(List<Division> romajiInfo, List<KaraokeWordInfo> wordInfo)
    {
        var elements = new List<JapaneseElement>();
        foreach (var division in romajiInfo)
        {
            elements.AddRange(division);
        }

        int delta = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            var curElement = elements[i].Element;
            var curHiraNotation = elements[i].HiraNotation;
        parseOneChar:
            if (i + delta >= wordInfo.Count)
            {
                if (!string.IsNullOrWhiteSpace(curHiraNotation))
                {
                    wordInfo[^1].Transliteration +=
                        Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                }

                break;
            }

            if (curElement.Contains(wordInfo[i + delta].CurrentWords.Trim()))
            {
                wordInfo[i + delta].Transliteration =
                    Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                if (!string.IsNullOrWhiteSpace(wordInfo[i + delta].CurrentWords))
                {
                    var trimmedWord = wordInfo[i + delta].CurrentWords.Trim();
                    var idx = curElement.IndexOf(trimmedWord, StringComparison.Ordinal);
                    if (idx >= 0)
                        curElement = curElement.Remove(idx, trimmedWord.Length);
                }

                if (curElement.Trim().Length > 0 && curHiraNotation.Length > 0)
                {
                    wordInfo[i + delta].Transliteration =
                        Utilities.ToRawRomaji(curHiraNotation[..1], RomajiSystem.Hepburn, true);
                    curHiraNotation = curHiraNotation[1..];
                    delta++;
                    goto parseOneChar;
                }
            }
        }
    }

    public static async Task ConvertRomaji(PureLyricInfo pureLyricInfo, List<SongLyric> lyrics)
    {
        switch (Ioc.Default.GetRequiredService<Setting>().LyricRomajiSource)
        {
            case RomajiSource.None:
                break;
            case RomajiSource.AutoSelect:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                else
                    await ConvertKawazuRomaji(lyrics);
                break;
            case RomajiSource.NeteaseOnly:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                break;
            case RomajiSource.KawazuOnly:
                await ConvertKawazuRomaji(lyrics);
                break;
        }
    }

    public static List<SongLyric> ConvertKaraok(PureLyricInfo pureLyricInfo)
    {
        if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo && !string.IsNullOrEmpty(karaokLyricInfo.KaraokLyric))
        {
            var parsedLyrics = KaraokeParser.ParseKaraoke(((KaraokLyricInfo)pureLyricInfo).KaraokLyric.AsSpan());
            if (Ioc.Default.GetRequiredService<Setting>().MigrateLyrics)
            {
                var pureLyrics = LrcParser.ParseLrc(pureLyricInfo.PureLyrics.AsSpan());
                var migrated = MigrationTool.Migrate(parsedLyrics, pureLyrics);
                return [.. migrated.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t })];
            }

            return [.. parsedLyrics.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t })];
        }

        throw new ArgumentException("HyLyricInfo is not KaraokeLyricInfo");
    }
    public static ALRCFile ConvertToALRC(List<SongLyric> lyric, double durationMs = 0)
    {
        var lines = new List<ALRCLine>();
        var alrc = new ALRCFile
        {
            Schema = "https://github.com/kengwang/ALRC/blob/main/schemas/v1.json",
            LyricInfo = null,
            SongInfo = null,
            Header = null,
            Lines = lines
        };
        var lastLine = new ALRCLine();
        foreach (var songLyric in lyric)
        {
            var line = new ALRCLine
            {
                Start = (long)songLyric.LyricLine.StartTime.TotalMilliseconds,
                LineStyle = null,
                RawText = songLyric.LyricLine.CurrentLyric,
                Transliteration = songLyric.Romaji?.Trim(),
                Translation = songLyric.Translation?.Trim()
            };
            lastLine.End = line.Start;
            lastLine = line;
            if (songLyric.LyricLine is KaraokeLyricsLine lrcLyricsLine)
            {
                line.Words = [.. lrcLyricsLine.WordInfos.Select(s => new ALRCWord
                {
                    Start = (long)s.StartTime.TotalMilliseconds,
                    End = (long)(s.StartTime + s.Duration).TotalMilliseconds,
                    Word = s.CurrentWords,
                    Transliteration = string.IsNullOrWhiteSpace(s.Transliteration) ? null : s.Transliteration
                })];
            }
            lines.Add(line);
        }

        if (lines.LastOrDefault() is { End: null or <= 0 } last) last.End = (long)durationMs;

        return alrc;
    }
}
