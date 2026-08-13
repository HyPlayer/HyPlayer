namespace HyPlayer.LyricEffects.Expressions;

public readonly record struct LyricColorValue(byte A, byte R, byte G, byte B)
{
    public static LyricColorValue Transparent { get; } = new(0, 0, 0, 0);

    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

public readonly record struct LyricExpressionLineFacto(
    int Index,
    int RelativeIndex,
    float IndexDistance);

public readonly record struct LyricExpressionLineStyle(
    bool Exists,
    string Position,
    bool HasColor,
    LyricColorValue Color,
    string Accent,
    bool HiddenOnBlur);

public readonly record struct LyricExpressionLine(
    int Index,
    int RelativeIndex,
    float IndexDistance,
    LyricExpressionLineFacto Facto,
    float ViewportDistance,
    bool IsActive,
    bool IsStarted,
    bool IsFinished,
    bool IsHovered,
    bool IsHidden,
    bool IsText,
    long StartMs,
    long EndMs,
    float Progress,
    float Width,
    float Height,
    float AnchorX,
    float AnchorY,
    string Text,
    LyricColorValue IdleColor,
    LyricColorValue FocusingColor,
    string Id,
    string ParentLineId,
    string LineStyle,
    string Comment,
    string RawText,
    string Transliteration,
    string Translation,
    LyricExpressionLineStyle Style)
{
    public long DurationMs => Math.Max(EndMs - StartMs, 0);
}

public readonly record struct LyricExpressionFrame(
    int CurrentLineIndex,
    long CurrentTimeMs,
    long RenderTimeMs,
    bool IsPlaying,
    bool IsScrolling,
    bool IsSeeking,
    float ScrollOffset,
    float ViewWidth,
    float ViewHeight,
    float Dpi,
    float Bpm);

public readonly record struct FocusedTextExpressionText(
    bool IsLyric,
    bool IsTransliteration,
    bool IsTranslation);

public readonly record struct FocusedTextExpressionWord(
    bool Exists,
    int Index,
    int Count,
    long StartTimeMs,
    long EndTimeMs,
    float Progress,
    bool IsInferred)
{
    public long DurationMs => Math.Max(EndTimeMs - StartTimeMs, 0);
}

public readonly record struct FocusedTextExpressionGlyph(
    int Index,
    int Count,
    int IndexInWord,
    int CountInWord,
    float RevealProgress,
    float LiftProgress,
    float VisualLeftDip,
    float VisualTopDip,
    float VisualRightDip,
    float VisualBottomDip,
    float VisualWidthDip,
    float VisualHeightDip);

public sealed class LyricExpressionFunctions
{
    public static LyricExpressionFunctions Instance { get; } = new();

    private LyricExpressionFunctions()
    {
    }

    public float Min(float left, float right) => MathF.Min(left, right);

    public float Max(float left, float right) => MathF.Max(left, right);

    public float Clamp(float value, float minimum, float maximum) => Math.Clamp(value, minimum, maximum);

    public float Lerp(float start, float end, float progress) => start + (end - start) * progress;

    public float SmoothStep(float start, float end, float progress)
    {
        var value = Math.Clamp(progress, 0, 1);
        value = value * value * (3 - 2 * value);
        return Lerp(start, end, value);
    }

    public float Abs(float value) => MathF.Abs(value);

    public float Sin(float value) => MathF.Sin(value);

    public float Cos(float value) => MathF.Cos(value);

    public float Pow(float value, float power) => MathF.Pow(value, power);

    public LyricColorValue Color(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var text = value.Trim();
        if (!text.StartsWith('#')) throw new FormatException("颜色必须使用 #RRGGBB 或 #AARRGGBB 格式。");

        return text.Length switch
        {
            7 => new LyricColorValue(255, ParseByte(text, 1), ParseByte(text, 3), ParseByte(text, 5)),
            9 => new LyricColorValue(ParseByte(text, 1), ParseByte(text, 3), ParseByte(text, 5), ParseByte(text, 7)),
            _ => throw new FormatException("颜色必须使用 #RRGGBB 或 #AARRGGBB 格式。")
        };
    }

    public LyricColorValue Rgba(float red, float green, float blue, float alpha)
    {
        return new LyricColorValue(
            ToByte(alpha <= 1 ? alpha * 255 : alpha),
            ToByte(red),
            ToByte(green),
            ToByte(blue));
    }

    public LyricColorValue LerpColor(LyricColorValue start, LyricColorValue end, float progress)
    {
        var amount = Math.Clamp(progress, 0, 1);
        return new LyricColorValue(
            ToByte(Lerp(start.A, end.A, amount)),
            ToByte(Lerp(start.R, end.R, amount)),
            ToByte(Lerp(start.G, end.G, amount)),
            ToByte(Lerp(start.B, end.B, amount)));
    }

    private static byte ParseByte(string value, int start) => Convert.ToByte(value.Substring(start, 2), 16);

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value), byte.MinValue, byte.MaxValue);
}
