using DynamicExpresso;
using DynamicExpresso.Exceptions;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace HyPlayer.LyricEffects.Expressions;

public delegate float LyricScalarExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    LyricExpressionFunctions fx);

public delegate LyricColorValue LyricColorExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    LyricExpressionFunctions fx);

public delegate string LyricTextExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    LyricExpressionFunctions fx);

public delegate float FocusedTextScalarExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    FocusedTextExpressionText text,
    FocusedTextExpressionWord word,
    FocusedTextExpressionGlyph glyph,
    LyricExpressionFunctions fx);

public delegate LyricColorValue FocusedTextColorExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    FocusedTextExpressionText text,
    FocusedTextExpressionWord word,
    FocusedTextExpressionGlyph glyph,
    LyricExpressionFunctions fx);

public delegate string FocusedTextTextExpression(
    LyricExpressionLine line,
    LyricExpressionFrame frame,
    FocusedTextExpressionText text,
    FocusedTextExpressionWord word,
    FocusedTextExpressionGlyph glyph,
    LyricExpressionFunctions fx);

public sealed record LyricExpressionDiagnostic(string Message, int Position, int Line, int Column);

public sealed class LyricExpressionCompileResult<TDelegate> where TDelegate : Delegate
{
    private LyricExpressionCompileResult(TDelegate? expression, LyricExpressionDiagnostic? diagnostic)
    {
        Expression = expression;
        Diagnostic = diagnostic;
    }

    public TDelegate? Expression { get; }

    public LyricExpressionDiagnostic? Diagnostic { get; }

    public bool IsSuccess => Expression is not null;

    public static LyricExpressionCompileResult<TDelegate> Success(TDelegate expression) => new(expression, null);

    public static LyricExpressionCompileResult<TDelegate> Failure(LyricExpressionDiagnostic diagnostic) => new(null, diagnostic);
}

public interface ILyricExpressionCompiler
{
    LyricExpressionCompileResult<LyricScalarExpression> CompileScalar(string source);

    LyricExpressionCompileResult<LyricColorExpression> CompileColor(string source);

    LyricExpressionCompileResult<LyricTextExpression> CompileText(string source);

    LyricExpressionCompileResult<FocusedTextScalarExpression> CompileFocusedScalar(string source);

    LyricExpressionCompileResult<FocusedTextColorExpression> CompileFocusedColor(string source);

    LyricExpressionCompileResult<FocusedTextTextExpression> CompileFocusedText(string source);
}

public sealed class LyricExpressionCompiler : ILyricExpressionCompiler
{
    public const int MaximumExpressionLength = 8 * 1024;

    private static readonly string[] ForbiddenTokens =
    [
        "=>", "typeof", "GetType", "System.", "Reflection", "Activator", "Assembly", "new ", "new(", "new\t", "new\r", "new\n", ";", "{", "}"
    ];

    private static readonly HashSet<string> AllowedFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Min", "Max", "Clamp", "Lerp", "SmoothStep", "Abs", "Sin", "Cos", "Pow", "Color", "Rgba", "LerpColor"
    };

    public LyricExpressionCompileResult<LyricScalarExpression> CompileScalar(string source) =>
        Compile<LyricScalarExpression>(source, ValidateScalar);

    public LyricExpressionCompileResult<LyricColorExpression> CompileColor(string source) =>
        Compile<LyricColorExpression>(source, ValidateColor);

    public LyricExpressionCompileResult<LyricTextExpression> CompileText(string source) =>
        Compile<LyricTextExpression>(source, ValidateText);

    public LyricExpressionCompileResult<FocusedTextScalarExpression> CompileFocusedScalar(string source) =>
        CompileFocused<FocusedTextScalarExpression>(source, ValidateFocusedScalar);

    public LyricExpressionCompileResult<FocusedTextColorExpression> CompileFocusedColor(string source) =>
        CompileFocused<FocusedTextColorExpression>(source, ValidateFocusedColor);

    public LyricExpressionCompileResult<FocusedTextTextExpression> CompileFocusedText(string source) =>
        CompileFocused<FocusedTextTextExpression>(source, ValidateFocusedText);

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LyricExpressionLine))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LyricExpressionFrame))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(LyricExpressionFunctions))]
    private static LyricExpressionCompileResult<TDelegate> Compile<TDelegate>(
        string source,
        Func<TDelegate, string?> sampleValidator) where TDelegate : Delegate
    {
        var preflight = Preflight(source);
        if (preflight is not null) return LyricExpressionCompileResult<TDelegate>.Failure(preflight);

        try
        {
            var interpreter = CreateInterpreter();
            var expression = interpreter.ParseAsExpression<TDelegate>(source, "line", "frame", "fx");
            var compiled = CompileExpression(expression);
            var validationError = sampleValidator(compiled);
            if (validationError is not null)
            {
                return LyricExpressionCompileResult<TDelegate>.Failure(
                    CreateDiagnostic(source, validationError, 0));
            }

            return LyricExpressionCompileResult<TDelegate>.Success(compiled);
        }
        catch (ParseException exception)
        {
            return LyricExpressionCompileResult<TDelegate>.Failure(
                CreateDiagnostic(source, exception.Message, Math.Max(exception.Position, 0)));
        }
        catch (Exception exception)
        {
            return LyricExpressionCompileResult<TDelegate>.Failure(
                CreateDiagnostic(source, exception.Message, 0));
        }
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(FocusedTextExpressionText))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(FocusedTextExpressionWord))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(FocusedTextExpressionGlyph))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LyricExpressionLine))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(LyricExpressionFrame))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(LyricExpressionFunctions))]
    private static LyricExpressionCompileResult<TDelegate> CompileFocused<TDelegate>(
        string source,
        Func<TDelegate, string?> sampleValidator) where TDelegate : Delegate
    {
        var preflight = Preflight(source);
        if (preflight is not null) return LyricExpressionCompileResult<TDelegate>.Failure(preflight);

        try
        {
            var interpreter = CreateInterpreter();
            var expression = interpreter.ParseAsExpression<TDelegate>(source, "line", "frame", "text", "word", "glyph", "fx");
            var compiled = CompileExpression(expression);
            var validationError = sampleValidator(compiled);
            return validationError is null
                ? LyricExpressionCompileResult<TDelegate>.Success(compiled)
                : LyricExpressionCompileResult<TDelegate>.Failure(CreateDiagnostic(source, validationError, 0));
        }
        catch (ParseException exception)
        {
            return LyricExpressionCompileResult<TDelegate>.Failure(
                CreateDiagnostic(source, exception.Message, Math.Max(exception.Position, 0)));
        }
        catch (Exception exception)
        {
            return LyricExpressionCompileResult<TDelegate>.Failure(CreateDiagnostic(source, exception.Message, 0));
        }
    }

    private static TDelegate CompileExpression<TDelegate>(Expression<TDelegate> expression) where TDelegate : Delegate =>
        expression.Compile(preferInterpretation: !RuntimeFeature.IsDynamicCodeCompiled);

    private static Interpreter CreateInterpreter() =>
        new Interpreter(InterpreterOptions.PrimitiveTypes | InterpreterOptions.SystemKeywords)
            .SetDefaultNumberType(DefaultNumberType.Single)
            .SetFunction("rgba", (Func<float, float, float, float, LyricColorValue>)Rgba);

    private static LyricColorValue Rgba(float red, float green, float blue, float alpha) =>
        LyricExpressionFunctions.Instance.Rgba(red, green, blue, alpha);

    private static LyricExpressionDiagnostic? Preflight(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return CreateDiagnostic(source, "表达式不能为空。", 0);
        if (source.Length > MaximumExpressionLength)
            return CreateDiagnostic(source, $"表达式不能超过 {MaximumExpressionLength} 个字符。", MaximumExpressionLength);

        foreach (var token in ForbiddenTokens)
        {
            var position = IndexOfOutsideString(source, token);
            if (position >= 0) return CreateDiagnostic(source, $"表达式包含不允许的语法“{token.Trim()}”。", position);
        }

        return ValidateOperatorsAndCalls(source);
    }

    private static LyricExpressionDiagnostic? ValidateOperatorsAndCalls(string source)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '=')
            {
                var previous = index == 0 ? '\0' : source[index - 1];
                var next = index + 1 >= source.Length ? '\0' : source[index + 1];
                if (previous is not ('=' or '!' or '<' or '>') && next != '=')
                    return CreateDiagnostic(source, "表达式不允许赋值。", index);
            }

            if (character != '(') continue;
            var nameEnd = index - 1;
            while (nameEnd >= 0 && char.IsWhiteSpace(source[nameEnd])) nameEnd--;
            var nameStart = nameEnd;
            while (nameStart >= 0 && (char.IsLetterOrDigit(source[nameStart]) || source[nameStart] == '_')) nameStart--;
            if (nameStart == nameEnd) continue;

            var name = source[(nameStart + 1)..(nameEnd + 1)];
            var isDirectRgba = name.Equals("rgba", StringComparison.OrdinalIgnoreCase) &&
                               !HasMemberReceiver(source, nameStart);
            if (!AllowedFunctionNames.Contains(name) || (!HasFxReceiver(source, nameStart) && !isDirectRgba))
                return CreateDiagnostic(source, $"只允许调用已注册的 fx 函数，不能调用“{name}”。", nameStart + 1);
        }

        return null;
    }

    private static bool HasFxReceiver(string source, int nameStart)
    {
        var dot = nameStart;
        while (dot >= 0 && char.IsWhiteSpace(source[dot])) dot--;
        if (dot < 0 || source[dot] != '.') return false;
        var receiverEnd = dot - 1;
        while (receiverEnd >= 0 && char.IsWhiteSpace(source[receiverEnd])) receiverEnd--;
        var receiverStart = receiverEnd;
        while (receiverStart >= 0 && (char.IsLetterOrDigit(source[receiverStart]) || source[receiverStart] == '_')) receiverStart--;
        return source[(receiverStart + 1)..(receiverEnd + 1)].Equals("fx", StringComparison.Ordinal);
    }

    private static bool HasMemberReceiver(string source, int nameStart)
    {
        var dot = nameStart;
        while (dot >= 0 && char.IsWhiteSpace(source[dot])) dot--;
        return dot >= 0 && source[dot] == '.';
    }

    private static int IndexOfOutsideString(string source, string token)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index <= source.Length - token.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (source.AsSpan(index).StartsWith(token, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return -1;
    }

    private static string? ValidateScalar(LyricScalarExpression expression)
    {
        foreach (var sample in LyricExpressionSamples.All)
        {
            var value = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance);
            if (!float.IsFinite(value)) return "表达式在示例状态下返回了 NaN 或 Infinity。";
        }

        return null;
    }

    private static string? ValidateColor(LyricColorExpression expression)
    {
        foreach (var sample in LyricExpressionSamples.All)
            _ = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance);
        return null;
    }

    private static string? ValidateText(LyricTextExpression expression)
    {
        foreach (var sample in LyricExpressionSamples.All)
        {
            if (expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance) is null)
                return "文本表达式不能返回 null。";
        }

        return null;
    }

    private static string? ValidateFocusedScalar(FocusedTextScalarExpression expression)
    {
        foreach (var sample in FocusedTextExpressionSamples.All)
        {
            var value = expression(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph, LyricExpressionFunctions.Instance);
            if (!float.IsFinite(value)) return "表达式在示例状态下返回了 NaN 或 Infinity。";
        }
        return null;
    }

    private static string? ValidateFocusedColor(FocusedTextColorExpression expression)
    {
        foreach (var sample in FocusedTextExpressionSamples.All)
            _ = expression(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph, LyricExpressionFunctions.Instance);
        return null;
    }

    private static string? ValidateFocusedText(FocusedTextTextExpression expression)
    {
        foreach (var sample in FocusedTextExpressionSamples.All)
        {
            if (expression(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph, LyricExpressionFunctions.Instance) is null)
                return "文本表达式不能返回 null。";
        }
        return null;
    }

    private static LyricExpressionDiagnostic CreateDiagnostic(string? source, string message, int position)
    {
        source ??= string.Empty;
        position = Math.Clamp(position, 0, source.Length);
        var line = 1;
        var column = 1;
        for (var index = 0; index < position; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else column++;
        }

        return new LyricExpressionDiagnostic(message, position, line, column);
    }
}

public readonly record struct FocusedTextExpressionSample(
    LyricExpressionLine Line,
    LyricExpressionFrame Frame,
    FocusedTextExpressionText Text,
    FocusedTextExpressionWord Word,
    FocusedTextExpressionGlyph Glyph);

public static class FocusedTextExpressionSamples
{
    public static IReadOnlyList<FocusedTextExpressionSample> All { get; } =
    [
        Create(true, 0.5f, 0.4f, false),
        Create(false, 1, 1, true)
    ];

    private static FocusedTextExpressionSample Create(
        bool wordExists,
        float reveal,
        float lift,
        bool translation)
    {
        var sample = LyricExpressionSamples.All[0];
        return new FocusedTextExpressionSample(
            sample.Line,
            sample.Frame,
            new FocusedTextExpressionText(!translation, false, translation),
            new FocusedTextExpressionWord(wordExists, 1, 4, 1000, 2000, 0.5f, true),
            new FocusedTextExpressionGlyph(2, 10, 1, 3, reveal, lift, 12, -20, 32, 4, 20, 24));
    }
}

public readonly record struct LyricExpressionSample(LyricExpressionLine Line, LyricExpressionFrame Frame);

public static class LyricExpressionSamples
{
    private static readonly LyricColorValue Idle = new(255, 180, 180, 180);
    private static readonly LyricColorValue Accent = new(255, 255, 255, 255);

    public static IReadOnlyList<LyricExpressionSample> All { get; } =
    [
        Create(index: 3, current: 3, active: true, scrolling: false, progress: 0.55f),
        Create(index: 5, current: 3, active: false, scrolling: false, progress: 0),
        Create(index: 1, current: 3, active: false, scrolling: true, progress: 1)
    ];

    private static LyricExpressionSample Create(int index, int current, bool active, bool scrolling, float progress)
    {
        var line = new LyricExpressionLine(
            index,
            index - current,
            Math.Abs(index - current),
            new LyricExpressionLineFacto(index, index - current, Math.Abs(index - current)),
            Math.Abs(index - current) * 0.18f,
            active,
            index <= current,
            index < current,
            false,
            false,
            true,
            10_000,
            14_000,
            progress,
            720,
            96,
            0,
            48,
            "示例歌词",
            Idle,
            Accent,
            $"line-{index}",
            string.Empty,
            "main",
            string.Empty,
            "示例歌词",
            string.Empty,
            string.Empty,
            new LyricExpressionLineStyle(true, "Left", true, Accent, "Normal", false));
        var frame = new LyricExpressionFrame(current, 12_000, 12_000, true, scrolling, false, 0, 1280, 720, 96, 120);
        return new LyricExpressionSample(line, frame);
    }
}
