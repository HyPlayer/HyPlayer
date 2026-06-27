#nullable enable

using Windows.Foundation;

namespace HyPlayer.LyricRenderer.Text;

public sealed class TextRenderFrame
{
    public int CurrentTokenIndex { get; init; }
    public float CurrentTokenProgress { get; init; }
    public Rect[] BeforeTokenBounds { get; init; } = [];
    public Rect[] CurrentTokenBounds { get; init; } = [];
    public Rect[] AfterTokenBounds { get; init; } = [];
    public Rect[] HighlightBounds { get; init; } = [];
    public Rect[] FullLineBounds { get; init; } = [];
    public Rect[] CharacterBounds { get; init; } = [];
    public float CurrentCharacterProgress { get; init; }
    public LyricTextToken? CurrentToken { get; init; }
}
