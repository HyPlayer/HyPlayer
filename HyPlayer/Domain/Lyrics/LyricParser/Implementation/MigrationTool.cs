using F23.StringSimilarity;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Domain.Lyrics.LyricParser.Implementation
{
    public static class MigrationTool
    {
        private static Cosine _cosine = new Cosine();
#nullable enable
        private static LyricLine? FindLyric(LyricLine lyricLine, ILyricCollection collection, double similarity, int range)
        {
            var sameTime = collection.Lines.Where(t => t.StartTime == lyricLine.StartTime);
            if (sameTime.Any())
            {
                return sameTime.First();
            }
            else
            {
                var lyrics = collection.Lines.Where(t => Math.Abs((t.StartTime - lyricLine.StartTime).TotalMilliseconds) <= range).ToList();
                if (!lyrics.Any())
                {
                    return default;
                }
                var similarityList = new List<double>();
                lyrics.ForEach(t =>
                {
                    similarityList.Add(_cosine.Similarity(lyricLine.LyricWithoutPunc, t.LyricWithoutPunc));
                });
                var value = similarityList.Max();
                var position = similarityList.IndexOf(value);
                if (value > similarity)
                {
                    lyrics[position].PossibleStartTime = lyricLine.StartTime;
                    return lyrics[position];
                }
            }
            return default;
        }
#nullable restore
        public static MigrateCollection Migrate(ILyricCollection target, ILyricCollection source, double similarity = 0.80, int range = 750)
        {
            var newLines = new List<LyricLine>();
            foreach (var line in source.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.CurrentLyric)) continue;
                var lyric = FindLyric(line, target, similarity, range);
                if (lyric != null)
                {
                    newLines.Add(lyric);
                }
                else
                {
                    newLines.Add(line);
                }
            }
            var result = new MigrateCollection(newLines.OrderBy(t => t.StartTime).ToList());
            return result;
        }
    }
}
