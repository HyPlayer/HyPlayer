namespace HyPlayer.LyricEffects.Expressions;

/// <summary>
/// Caches immutable focused expressions at the narrowest context they read for one rendered frame.
/// Dictionary capacities survive <see cref="Clear"/>, so steady-state rendering does not allocate.
/// </summary>
public sealed class FocusedTextExpressionFrameCache
{
    private readonly Dictionary<int, float> _scalarFrame = [];
    private readonly Dictionary<ScopedKey, float> _scalarScoped = [];
    private readonly Dictionary<int, LyricColorValue> _colorFrame = [];
    private readonly Dictionary<ScopedKey, LyricColorValue> _colorScoped = [];
    private readonly Dictionary<int, string> _textFrame = [];
    private readonly Dictionary<ScopedKey, string> _textScoped = [];

    public float EvaluateScalar(
        int expressionId,
        FocusedTextExpressionDependencies dependencies,
        FocusedTextScalarExpression expression,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph)
    {
        if (TryGetScope(dependencies, expressionId, text, word, glyph, out var key))
        {
            if (_scalarScoped.TryGetValue(key, out var cached)) return cached;
            var value = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
            _scalarScoped.Add(key, value);
            return value;
        }

        if (_scalarFrame.TryGetValue(expressionId, out var frameCached)) return frameCached;
        var frameValue = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
        _scalarFrame.Add(expressionId, frameValue);
        return frameValue;
    }

    public LyricColorValue EvaluateColor(
        int expressionId,
        FocusedTextExpressionDependencies dependencies,
        FocusedTextColorExpression expression,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph)
    {
        if (TryGetScope(dependencies, expressionId, text, word, glyph, out var key))
        {
            if (_colorScoped.TryGetValue(key, out var cached)) return cached;
            var value = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
            _colorScoped.Add(key, value);
            return value;
        }

        if (_colorFrame.TryGetValue(expressionId, out var frameCached)) return frameCached;
        var frameValue = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
        _colorFrame.Add(expressionId, frameValue);
        return frameValue;
    }

    public string EvaluateText(
        int expressionId,
        FocusedTextExpressionDependencies dependencies,
        FocusedTextTextExpression expression,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph)
    {
        if (TryGetScope(dependencies, expressionId, text, word, glyph, out var key))
        {
            if (_textScoped.TryGetValue(key, out var cached)) return cached;
            var value = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
            _textScoped.Add(key, value);
            return value;
        }

        if (_textFrame.TryGetValue(expressionId, out var frameCached)) return frameCached;
        var frameValue = expression(line, frame, text, word, glyph, LyricExpressionFunctions.Instance);
        _textFrame.Add(expressionId, frameValue);
        return frameValue;
    }

    public void Clear()
    {
        _scalarFrame.Clear();
        _scalarScoped.Clear();
        _colorFrame.Clear();
        _colorScoped.Clear();
        _textFrame.Clear();
        _textScoped.Clear();
    }

    private static bool TryGetScope(
        FocusedTextExpressionDependencies dependencies,
        int expressionId,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph,
        out ScopedKey key)
    {
        var layer = text.IsLyric ? 0 : text.IsTransliteration ? 1 : 2;
        if ((dependencies & FocusedTextExpressionDependencies.Glyph) != 0)
        {
            key = new ScopedKey(expressionId, layer, glyph.Index);
            return true;
        }
        if ((dependencies & FocusedTextExpressionDependencies.Word) != 0)
        {
            key = new ScopedKey(expressionId, layer, word.Index);
            return true;
        }
        if ((dependencies & FocusedTextExpressionDependencies.Text) != 0)
        {
            key = new ScopedKey(expressionId, layer, 0);
            return true;
        }

        key = default;
        return false;
    }

    private readonly record struct ScopedKey(int ExpressionId, int Layer, int ContextIndex);
}
