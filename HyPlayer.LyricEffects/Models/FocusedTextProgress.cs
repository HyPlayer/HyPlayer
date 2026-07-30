namespace HyPlayer.LyricEffects.Models;

public enum GlyphLiftMotion
{
    Hold,
    Pulse
}

public static class FocusedTextProgress
{
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

    public static float GetMotionProgress(
        float wordProgress,
        long wordDurationMs,
        int glyphIndexInWord,
        int glyphCountInWord,
        float overlap,
        float wholeWordThresholdMs,
        GlyphLiftMotion motion)
    {
        var local = wordDurationMs <= wholeWordThresholdMs
            ? Math.Clamp(wordProgress, 0, 1)
            : GetGlyphWindowProgress(wordProgress, glyphIndexInWord, glyphCountInWord, overlap);
        if (motion == GlyphLiftMotion.Hold) return local;
        return local <= 0.5f ? local * 2 : (1 - local) * 2;
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
