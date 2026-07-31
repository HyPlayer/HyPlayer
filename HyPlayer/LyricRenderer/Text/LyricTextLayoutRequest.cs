#nullable enable

using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Text;

public sealed class LyricTextLayoutRequest
{
    public required CanvasDrawingSession Session { get; init; }
    public required RenderContext Context { get; init; }
    public required RenderTypography Typography { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<LyricTextToken> Tokens { get; init; }
    public string? Translation { get; init; }
    public string? Transliteration { get; init; }
    public bool HiddenOnBlur { get; init; }
    public float TextPadding { get; init; }
    public float LiftAmount { get; init; }
    public Color FocusingColor { get; init; }
    public float CanvasHeight { get; init; }
    public TextAlignment Alignment { get; init; }
    public float LyricFontSize { get; init; }
    public float TranslationFontSize { get; init; }
    public float TransliterationFontSize { get; init; }
    public string? FontFamily { get; init; }
}