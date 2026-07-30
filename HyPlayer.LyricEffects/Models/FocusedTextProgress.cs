namespace HyPlayer.LyricEffects.Models;

public enum GlyphLiftMotion
{
    Hold,
    Pulse
}

public static class FocusedTextProgress
{
    public static double GetElasticProgress(
        double normalizedTime,
        double springiness,
        double oscillations)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;
        if (springiness == 0) return normalizedTime;

        var remaining = 1 - normalizedTime;
        var envelope = (Math.Exp(springiness * remaining) - 1) /
                       (Math.Exp(springiness) - 1);
        return 1 - envelope * Math.Cos(Math.PI * 2 * oscillations * remaining);
    }

    public static float GetGlyphWindowProgress(
        float progress,
        int glyphIndex,
        int glyphCount,
        float overlap)
    {
        progress = Math.Clamp(progress, 0, 1);
        glyphCount = Math.Max(glyphCount, 1);
        glyphIndex = Math.Clamp(glyphIndex, 0, glyphCount - 1);
        overlap = Math.Clamp(overlap, 0, 1);
        if (glyphCount == 1) return progress;

        var window = 1f / (1f + (glyphCount - 1) * (1f - overlap));
        var step = window * (1f - overlap);
        return Math.Clamp((progress - glyphIndex * step) / window, 0, 1);
    }

    public static float GetLiftProgress(
        float wordProgress,
        long wordDurationMs,
        int glyphIndexInWord,
        int glyphCountInWord,
        float overlap,
        float wholeWordThresholdMs,
        GlyphLiftUnit liftUnit,
        GlyphLiftMotion motion)
    {
        var liftAsWord = liftUnit == GlyphLiftUnit.Word ||
                         liftUnit == GlyphLiftUnit.Auto && wordDurationMs <= wholeWordThresholdMs;
        var local = liftAsWord
            ? Math.Clamp(wordProgress, 0, 1)
            : GetGlyphWindowProgress(wordProgress, glyphIndexInWord, glyphCountInWord, overlap);
        if (motion == GlyphLiftMotion.Hold) return local;
        return local <= 0.5f ? local * 2 : (1 - local) * 2;
    }

    public static float GetTimedProgress(
        long timeMs,
        long startMs,
        long endMs,
        float startOffsetMs,
        float finishDurationMs,
        GlyphLiftMotion motion)
    {
        var liftStart = startMs - startOffsetMs;
        var liftEnd = endMs + finishDurationMs;
        if (liftEnd <= liftStart)
            return motion == GlyphLiftMotion.Hold && timeMs >= liftStart ? 1 : 0;
        return Math.Clamp((timeMs - liftStart) / (liftEnd - liftStart), 0, 1);
    }

    public static float GetRevealProgress(
        HighlightRevealMode mode,
        float wordProgress,
        int glyphIndexInWord,
        int glyphCountInWord) => mode switch
        {
            HighlightRevealMode.GlyphStep =>
                GetGlyphWindowProgress(wordProgress, glyphIndexInWord, glyphCountInWord, 0),
            HighlightRevealMode.WholeWord => Math.Clamp(wordProgress, 0, 1),
            _ => Math.Clamp(wordProgress * Math.Max(glyphCountInWord, 1) - glyphIndexInWord, 0, 1)
        };
}
