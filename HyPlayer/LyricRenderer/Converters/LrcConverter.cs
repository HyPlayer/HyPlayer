using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.Classes;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using LyricParser.Abstraction;

namespace HyPlayer.LyricRenderer.Converters;

public static class LrcConverter
{
    public static List<RenderingLyricLine> Convert(List<SongLyric> lines)
    {
        var result = new List<RenderingLyricLine>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lyricLine = line.LyricLine;
            long endTime = -1;
            if (lyricLine is KaraokeLyricsLine karaokeLyricsLine)
                endTime = (long)(karaokeLyricsLine.StartTime.TotalMilliseconds +
                                 karaokeLyricsLine.Duration.TotalMilliseconds);
            
            if (endTime is -1) endTime = lines.Count > index + 1 ? (long)lines[index + 1].LyricLine.StartTime.TotalMilliseconds : int.MaxValue;
            long startTime = (long)line.LyricLine.StartTime.TotalMilliseconds;
            if (lyricLine is KaraokeLyricsLine syllableLineInfo)
            {
                var syllables = syllableLineInfo.WordInfos.Select(t => new RenderingSyllable
                {
                    Syllable = t.CurrentWords,
                    StartTime = (long)t.StartTime.TotalMilliseconds,
                    EndTime = (long)t.StartTime.TotalMilliseconds + (long)t.Duration.TotalMilliseconds,
                }).ToList();
                if (syllables.Count > 0 && !syllables.All(t=>string.IsNullOrWhiteSpace(t.Syllable)))
                {
                    result.Add(new SyllablesRenderingLyricLine
                    {
                        Id = index,
                        HiddenOnBlur = false,
                        KeyFrames =
                        [
                            startTime,
                            endTime
                        ],
                        StartTime = startTime,
                        EndTime = endTime,
                        Syllables = syllables,
                        Transliteration = line.HaveRomaji ? line.Romaji : null,
                        Translation = line.HaveTranslation ? line.Translation : null
                    });
                }
                else
                    result.Add(new ProgressBarRenderingLyricLine
                    {
                        Id = index,
                        KeyFrames =
                        [
                            startTime,
                            endTime
                        ],
                        StartTime = startTime,
                        EndTime = endTime,
                        HiddenOnBlur = true,
                    });
                
                continue;
            }

            
            if (!string.IsNullOrWhiteSpace(line.LyricLine.CurrentLyric))
            {
                
                result.Add(new TextRenderingLyricLine
                {
                    Id = index,
                    KeyFrames =
                    [
                        startTime,
                        endTime
                    ],
                    StartTime = startTime,
                    EndTime = endTime,
                    Text = line.LyricLine.CurrentLyric,
                    Transliteration = line.HaveRomaji ? line.Romaji : null,
                    Translation = line.HaveTranslation ? line.Translation : null
                });
            }
            else
                result.Add(new ProgressBarRenderingLyricLine
                {
                    Id = index,
                    KeyFrames =
                    [
                        startTime,
                        endTime
                    ],
                    StartTime = startTime,
                    EndTime = endTime,
                    HiddenOnBlur = true,
                });
        }

        return result;
    }
}