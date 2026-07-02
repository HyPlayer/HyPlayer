#nullable enable

using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace HyPlayer.LyricRenderer.Text;

internal sealed partial class LyricGlyphPlanCollector(
    LyricTextLayer layer,
    IReadOnlyList<int> sourceIndexMap,
    IReadOnlyList<int> tokenIndexMap,
    float dpi) : ICanvasTextRenderer
{
    private readonly List<LyricGlyphCluster> _clusters = [];

    public IReadOnlyList<LyricGlyphCluster> Clusters => _clusters;

    public float Dpi => dpi;

    public bool PixelSnappingDisabled => false;

    public Matrix3x2 Transform => Matrix3x2.Identity;

    public void DrawGlyphRun(
        Vector2 point,
        CanvasFontFace fontFace,
        float fontSize,
        CanvasGlyph[] glyphs,
        bool isSideways,
        uint bidiLevel,
        object brush,
        CanvasTextMeasuringMode measuringMode,
        string localeName,
        string textString,
        int[] clusterMap,
        uint characterIndex,
        CanvasGlyphOrientation glyphOrientation)
    {
        if (glyphs is null || glyphs.Length == 0 || textString.Length == 0 || clusterMap.Length == 0)
        {
            return;
        }

        var advanceOrigins = CreateAdvanceOrigins(glyphs);
        var consumedCharacters = 0;
        while (consumedCharacters < textString.Length && consumedCharacters < clusterMap.Length)
        {
            var glyphStart = Math.Clamp(clusterMap[consumedCharacters], 0, glyphs.Length - 1);
            var charStart = consumedCharacters;
            consumedCharacters++;

            while (consumedCharacters < textString.Length &&
                   consumedCharacters < clusterMap.Length &&
                   clusterMap[consumedCharacters] == glyphStart)
            {
                consumedCharacters++;
            }

            var charEnd = consumedCharacters;
            var glyphEnd = FindNextGlyphStart(clusterMap, charEnd, glyphStart, glyphs.Length);
            if (glyphEnd <= glyphStart)
            {
                glyphEnd = Math.Min(glyphStart + 1, glyphs.Length);
            }

            var clusterGlyphs = new CanvasGlyph[glyphEnd - glyphStart];
            Array.Copy(glyphs, glyphStart, clusterGlyphs, 0, clusterGlyphs.Length);

            var sourceStart = int.MaxValue;
            var sourceEnd = -1;
            var tokenStartIndex = int.MaxValue;
            var tokenEndIndexExclusive = -1;
            var layoutCharacterStart = (int)characterIndex + charStart;
            var layoutCharacterEnd = (int)characterIndex + charEnd;
            for (var layoutIndex = layoutCharacterStart; layoutIndex < layoutCharacterEnd; layoutIndex++)
            {
                if ((uint)layoutIndex >= (uint)sourceIndexMap.Count)
                {
                    continue;
                }

                var sourceIndex = sourceIndexMap[layoutIndex];
                if (sourceIndex < 0)
                {
                    continue;
                }

                sourceStart = Math.Min(sourceStart, sourceIndex);
                sourceEnd = Math.Max(sourceEnd, sourceIndex + 1);
                if ((uint)sourceIndex < (uint)tokenIndexMap.Count && tokenIndexMap[sourceIndex] >= 0)
                {
                    var tokenIndex = tokenIndexMap[sourceIndex];
                    tokenStartIndex = Math.Min(tokenStartIndex, tokenIndex);
                    tokenEndIndexExclusive = Math.Max(tokenEndIndexExclusive, tokenIndex + 1);
                }
            }

            _clusters.Add(new LyricGlyphCluster
            {
                Layer = layer,
                BaseState = new LyricGlyphDrawState
                {
                    Layer = layer,
                    FontFace = fontFace,
                    FontSize = fontSize,
                    Glyphs = clusterGlyphs,
                    Origin = new Vector2(point.X + advanceOrigins[glyphStart], point.Y),
                    IsSideways = isSideways,
                    BidiLevel = bidiLevel,
                    MeasuringMode = measuringMode,
                    LocaleName = localeName,
                    TextString = textString[charStart..charEnd],
                    ClusterMap = CreateClusterMap(clusterMap, charStart, charEnd, glyphStart),
                    CharacterIndex = (uint)(characterIndex + charStart),
                    GlyphOrientation = glyphOrientation,
                    Opacity = 1,
                    BlurRadius = 0,
                    Scale = 1
                },
                SourceStart = sourceStart == int.MaxValue ? -1 : sourceStart,
                SourceEnd = sourceEnd,
                TokenStartIndex = tokenStartIndex == int.MaxValue ? -1 : tokenStartIndex,
                TokenEndIndexExclusive = tokenEndIndexExclusive
            });
        }
    }

    public void DrawStrikethrough(
        Vector2 point,
        float strikethroughWidth,
        float strikethroughThickness,
        float strikethroughOffset,
        CanvasTextDirection textDirection,
        object brush,
        CanvasTextMeasuringMode measuringMode,
        string localeName,
        CanvasGlyphOrientation glyphOrientation)
    {
    }

    public void DrawUnderline(
        Vector2 point,
        float underlineWidth,
        float underlineThickness,
        float underlineOffset,
        float runHeight,
        CanvasTextDirection textDirection,
        object brush,
        CanvasTextMeasuringMode measuringMode,
        string localeName,
        CanvasGlyphOrientation glyphOrientation)
    {
    }

    public void DrawInlineObject(
        Vector2 point,
        ICanvasTextInlineObject inlineObject,
        bool isSideways,
        bool isRightToLeft,
        object brush,
        CanvasGlyphOrientation glyphOrientation)
    {
    }

    public static void FinalizeClusterIndexes(IReadOnlyList<LyricGlyphCluster> clusters, int tokenCount)
    {
        var tokenCounts = tokenCount > 0 ? new int[tokenCount] : [];
        for (var i = 0; i < clusters.Count; i++)
        {
            clusters[i].LayerClusterIndex = i;
            clusters[i].LayerClusterCount = clusters.Count;
            var tokenIndex = clusters[i].TokenStartIndex;
            if ((uint)tokenIndex < (uint)tokenCounts.Length)
            {
                clusters[i].TokenClusterIndex = tokenCounts[tokenIndex];
                tokenCounts[tokenIndex]++;
            }
        }

        for (var i = 0; i < clusters.Count; i++)
        {
            var tokenIndex = clusters[i].TokenStartIndex;
            clusters[i].TokenClusterCount = (uint)tokenIndex < (uint)tokenCounts.Length
                ? tokenCounts[tokenIndex]
                : clusters.Count;
        }
    }

    private static float[] CreateAdvanceOrigins(IReadOnlyList<CanvasGlyph> glyphs)
    {
        var origins = new float[glyphs.Count];
        var current = 0f;
        for (var i = 0; i < glyphs.Count; i++)
        {
            origins[i] = current;
            current += glyphs[i].Advance;
        }

        return origins;
    }

    private static int FindNextGlyphStart(IReadOnlyList<int> clusterMap, int charStart, int glyphStart, int glyphCount)
    {
        var next = glyphCount;
        for (var i = charStart; i < clusterMap.Count; i++)
        {
            var candidate = clusterMap[i];
            if (candidate > glyphStart && candidate < next)
            {
                next = candidate;
            }
        }

        return next;
    }

    private static int[] CreateClusterMap(IReadOnlyList<int> clusterMap, int charStart, int charEnd, int glyphStart)
    {
        var result = new int[charEnd - charStart];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Math.Max(0, clusterMap[charStart + i] - glyphStart);
        }

        return result;
    }
}
