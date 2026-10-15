using System;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public static class TapAligner
{
    public static double? FindMap(PlaybackTap tap, MixRegion region, long expectedRegionIndex)
    {
        var needleFrames = region.SampleRate / 4;
        var needle = new float[needleFrames * 2];
        var tapStart = tap.CopyLatest(needle);
        if (tap.TotalFrames - tapStart < needleFrames) return null;

        double energy = 0;
        for (var i = 0; i < needleFrames; i++) energy += needle[i * 2] * (double)needle[i * 2];
        if (energy < 1e-6) return null;

        var expectedStart = expectedRegionIndex - needleFrames;
        var offset = Search(region, needle, needleFrames, expectedStart, region.SampleRate * 2, 0.8);
        if (offset < 0) offset = Search(region, needle, needleFrames, expectedStart, region.SampleRate * 8, 0.9);
        return offset < 0 ? null : offset - tapStart;
    }

    private static long Search(MixRegion region, float[] needle, int needleFrames, long expectedStart, int radius, double threshold)
    {
        var from = Math.Max(0, expectedStart - radius);
        var to = Math.Min(region.Frames - needleFrames, expectedStart + radius);
        if (to <= from) return -1;

        var bestOffset = -1L;
        var bestScore = double.NegativeInfinity;
        for (var offset = from; offset <= to; offset += 4)
        {
            var score = Score(region.Samples, offset, needle, needleFrames, 8);
            if (score > bestScore) { bestScore = score; bestOffset = offset; }
        }
        if (bestOffset < 0) return -1;
        var refineFrom = Math.Max(from, bestOffset - 8);
        var refineTo = Math.Min(to, bestOffset + 8);
        bestScore = double.NegativeInfinity;
        for (var offset = refineFrom; offset <= refineTo; offset++)
        {
            var score = Score(region.Samples, offset, needle, needleFrames, 1);
            if (score > bestScore) { bestScore = score; bestOffset = offset; }
        }
        return bestScore < threshold ? -1 : bestOffset;
    }

    private static double Score(float[] region, long offset, float[] needle, int frames, int step)
    {
        double dot = 0, rr = 0, nn = 0;
        for (var i = 0; i < frames; i += step)
        {
            var r = region[(offset + i) * 2];
            var n = needle[i * 2];
            dot += r * n;
            rr += r * (double)r;
            nn += n * (double)n;
        }
        return dot / Math.Sqrt(rr * nn + 1e-12);
    }
}
