namespace HyPlayer.LyricEffects.Models;

public readonly record struct FocusedEffectOutsets(float Left, float Top, float Right, float Bottom)
{
    public static FocusedEffectOutsets None => new(0, 0, 0, 0);
}

public readonly record struct FocusedRevealClip(float Left, float Top, float Width, float Height)
{
    public float Right => Left + Width;
    public float Bottom => Top + Height;
}

public static class FocusedRevealClipCalculator
{
    public static FocusedRevealClip? GetContributionClip(
        float visualLeft,
        float visualTop,
        float visualWidth,
        float visualHeight,
        float revealProgress,
        bool highlighted,
        bool isRightToLeft,
        FocusedEffectOutsets effectOutsets)
    {
        var reveal = Math.Clamp(revealProgress, 0, 1);
        if ((highlighted && reveal >= 1) || (!highlighted && reveal <= 0))
        {
            // The contribution owns the complete GlyphUnit. Clipping it to the
            // visual bounds would cut off blur, glow, stroke, and shadow.
            return null;
        }

        effectOutsets = new FocusedEffectOutsets(
            Math.Max(effectOutsets.Left, 0),
            Math.Max(effectOutsets.Top, 0),
            Math.Max(effectOutsets.Right, 0),
            Math.Max(effectOutsets.Bottom, 0));

        var visualRight = visualLeft + Math.Max(visualWidth, 0);
        var visualBottom = visualTop + Math.Max(visualHeight, 0);
        var highlightedWidth = Math.Max(visualWidth, 0) * reveal;
        var boundary = isRightToLeft
            ? visualRight - highlightedWidth
            : visualLeft + highlightedWidth;

        float left;
        float right;
        if (highlighted)
        {
            left = isRightToLeft ? boundary : visualLeft - effectOutsets.Left;
            right = isRightToLeft ? visualRight + effectOutsets.Right : boundary;
        }
        else
        {
            left = isRightToLeft ? visualLeft - effectOutsets.Left : boundary;
            right = isRightToLeft ? boundary : visualRight + effectOutsets.Right;
        }

        var top = visualTop - effectOutsets.Top;
        var bottom = visualBottom + effectOutsets.Bottom;
        return new FocusedRevealClip(
            left,
            top,
            Math.Max(right - left, 0),
            Math.Max(bottom - top, 0));
    }
}
