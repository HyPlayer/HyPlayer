using HyPlayer.LyricRenderer.Text;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class LyricGlyphBoundaryTests
{
    [Test]
    public void TokenOverlap_ShouldMatchFullScanWithEmptyTokensAndUnorderedSourceRanges()
    {
        var random = new Random(71);
        for (var test = 0; test < 10000; test++)
        {
            var boundaries = new int[random.Next(1, 100)];
            for (var i = 1; i < boundaries.Length; i++)
                boundaries[i] = boundaries[i - 1] + random.Next(0, 5);
            var start = random.Next(-1, boundaries[^1] + 5);
            var end = start + random.Next(0, 20);
            var first = -1;
            var exclusiveEnd = -1;
            if (start >= 0 && end > start)
                for (var i = 0; i < boundaries.Length - 1; i++)
                {
                    if (boundaries[i + 1] <= start || boundaries[i] >= end) continue;
                    if (first < 0) first = i;
                    exclusiveEnd = i + 1;
                }
            if (Win2DLyricTextLayouter.FindOverlappingTokens(boundaries, start, end) != (first, exclusiveEnd))
                throw new InvalidOperationException("Source/token overlap semantics changed.");
        }
    }

    [Test]
    public void AdjacentGlyphBoundary_ShouldStopReadingTheRemainingRun()
    {
        var map = new CountingMap(256);
        for (var i = 0; i < map.Count; i++)
            if (LyricGlyphPlanCollector.FindNextGlyphStart(map, i + 1, i, map.Count) != i + 1)
                throw new InvalidOperationException("CJK glyph boundary changed.");
        if (map.Reads != map.Count - 1)
            throw new InvalidOperationException($"Expected a linear scan, got {map.Reads} reads.");
    }

    [Test]
    public void GlyphBoundary_ShouldPreserveRepeatedAndUnorderedMappings()
    {
        var random = new Random(42);
        for (var test = 0; test < 10000; test++)
        {
            var count = random.Next(1, 150);
            var map = new int[count];
            for (var i = 0; i < count; i++) map[i] = random.Next(count);
            var start = random.Next(count + 1);
            var glyph = random.Next(count);
            var expected = count;
            for (var i = start; i < count; i++)
                if (map[i] > glyph) expected = Math.Min(expected, map[i]);
            if (LyricGlyphPlanCollector.FindNextGlyphStart(map, start, glyph, count) != expected)
                throw new InvalidOperationException("Repeated/nonmonotone glyph mapping changed.");
        }
    }

    [Test]
    public void CurrentTokenLookup_ShouldReturnLastTokenAtDuplicateTimestamp()
    {
        IReadOnlyList<long> starts = (long[])[100, 200, 200, 400];
        if (DefaultTextProgressResolver.FindCurrentTokenIndex(starts, 99) != -1 ||
            DefaultTextProgressResolver.FindCurrentTokenIndex(starts, 100) != 0 ||
            DefaultTextProgressResolver.FindCurrentTokenIndex(starts, 200) != 2 ||
            DefaultTextProgressResolver.FindCurrentTokenIndex(starts, 399) != 2 ||
            DefaultTextProgressResolver.FindCurrentTokenIndex(starts, 400) != 3 ||
            DefaultTextProgressResolver.FindCurrentTokenIndex((long[])[300, 100, 200], 250, false) != 2)
            throw new InvalidOperationException("Token upper-bound lookup changed its duplicate timestamp semantics.");
    }

    private sealed class CountingMap(int count) : IReadOnlyList<int>
    {
        public int Count => count;
        public int Reads { get; private set; }
        public int this[int index] { get { Reads++; return index; } }
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
