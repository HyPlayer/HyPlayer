#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace HyPlayer.LyricRenderer.Text;

public sealed partial class LyricTextLayoutSnapshot : IDisposable
{
    public required string Text { get; init; }
    public required IReadOnlyList<LyricTextToken> Tokens { get; init; }
    public required bool HasRealWords { get; init; }
    public required IReadOnlyList<LyricTextToken> InferredTransliterationTokens { get; init; }
    public required IReadOnlyList<LyricTextToken> InferredTranslationTokens { get; init; }
    public required CanvasTextLayout TextLayout { get; init; }
    public CanvasTextLayout? TranslationLayout { get; init; }
    public CanvasTextLayout? TransliterationLayout { get; init; }
    public required ICanvasImage StaticPersistCache { get; init; }
    public required ICanvasImage DefaultTextPersistCache { get; init; }
    public ICanvasImage? DefaultTransliterationPersistCache { get; init; }
    public ICanvasImage? DefaultTranslationPersistCache { get; init; }
    public required Rect SizePixelRect { get; init; }
    public required float TextRenderActualTop { get; init; }
    public required float TranslationRenderActualTop { get; init; }
    public required float DrawingOffsetY { get; init; }
    public required float RenderingWidth { get; init; }
    public required float RenderingHeight { get; init; }
    public required float ScalingCenterX { get; init; }
    public required Windows.UI.Color IdleColor { get; init; }
    public required Windows.UI.Color FocusingColor { get; init; }
    public required IReadOnlyList<LyricGlyphCluster> LyricGlyphClusters { get; init; }
    public required IReadOnlyList<LyricGlyphCluster> TransliterationGlyphClusters { get; init; }
    public required IReadOnlyList<LyricGlyphCluster> TranslationGlyphClusters { get; init; }

    public void Dispose()
    {
        TextLayout.Dispose();
        TranslationLayout?.Dispose();
        TransliterationLayout?.Dispose();
        (StaticPersistCache as IDisposable)?.Dispose();
        (DefaultTextPersistCache as IDisposable)?.Dispose();
        (DefaultTransliterationPersistCache as IDisposable)?.Dispose();
        (DefaultTranslationPersistCache as IDisposable)?.Dispose();
    }
}
