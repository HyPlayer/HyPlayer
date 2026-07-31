#nullable enable

namespace HyPlayer.LyricRenderer.Text;

public sealed class TextRenderFrame
{
    public int CurrentTokenIndex { get; init; }
    public float CurrentTokenProgress { get; init; }
    public long CurrentTokenDuration { get; init; }
    public float LineProgress { get; init; }
    public float CurrentLyricSourcePosition { get; init; }
    public float CurrentTransliterationSourcePosition { get; init; }
    public LyricTextToken? CurrentToken { get; init; }
}