using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricEffects.Drawing;

public sealed record LyricDrawScriptCommand(string Name, IReadOnlyList<string> Arguments, int Position, int Line);

public sealed record LyricDrawScriptDiagnostic(string Message, int Position, int Line, int Column);

public sealed class LyricDrawScriptParseResult
{
    private LyricDrawScriptParseResult(IReadOnlyList<LyricDrawScriptCommand> commands, LyricDrawScriptDiagnostic? diagnostic)
    {
        Commands = commands;
        Diagnostic = diagnostic;
    }

    public IReadOnlyList<LyricDrawScriptCommand> Commands { get; }

    public LyricDrawScriptDiagnostic? Diagnostic { get; }

    public bool IsSuccess => Diagnostic is null;

    public static LyricDrawScriptParseResult Success(IReadOnlyList<LyricDrawScriptCommand> commands) => new(commands, null);

    public static LyricDrawScriptParseResult Failure(LyricDrawScriptDiagnostic diagnostic) => new([], diagnostic);
}

public sealed record LyricDrawCommandSignature(string Name, IReadOnlyList<LyricExpressionValueType> Arguments);

public interface ILyricDrawScriptParser
{
    LyricDrawScriptParseResult Parse(string source);
}

public sealed class LyricDrawScriptParser : ILyricDrawScriptParser
{
    public const int MaximumScriptLength = 64 * 1024;
    public const int MaximumCommandCount = 256;

    public LyricDrawScriptParseResult Parse(string source)
    {
        source ??= string.Empty;
        if (source.Length > MaximumScriptLength)
            return Failure(source, "绘图脚本过长。", MaximumScriptLength);

        var statements = SplitTopLevel(RemoveComments(source), source, out var splitDiagnostic);
        if (splitDiagnostic is not null) return LyricDrawScriptParseResult.Failure(splitDiagnostic);
        if (statements.Count > MaximumCommandCount)
            return Failure(source, $"绘图脚本最多允许 {MaximumCommandCount} 条命令。", statements[MaximumCommandCount].Position);

        var commands = new List<LyricDrawScriptCommand>(statements.Count);
        foreach (var statement in statements)
        {
            var text = statement.Text.Trim();
            if (text.Length == 0) continue;
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open <= 0 || close != text.Length - 1)
                return Failure(source, "绘图命令必须使用 Name(...) 格式。", statement.Position);

            var name = text[..open].Trim();
            if (!IsIdentifier(name)) return Failure(source, $"无效的绘图命令名“{name}”。", statement.Position);

            var argumentText = text[(open + 1)..close];
            var arguments = SplitArguments(argumentText, source, statement.Position + open + 1, out var argumentDiagnostic);
            if (argumentDiagnostic is not null) return LyricDrawScriptParseResult.Failure(argumentDiagnostic);

            var (line, _) = GetLineColumn(source, statement.Position);
            commands.Add(new LyricDrawScriptCommand(name, arguments, statement.Position, line));
        }

        return LyricDrawScriptParseResult.Success(commands);
    }

    private static string RemoveComments(string source)
    {
        var result = new System.Text.StringBuilder(source.Length);
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (quote != '\0')
            {
                result.Append(current);
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                result.Append(current);
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    result.Append(' ');
                    index++;
                }

                if (index < source.Length) result.Append('\n');
                continue;
            }

            result.Append(current);
        }

        return result.ToString();
    }

    private static List<Statement> SplitTopLevel(string cleaned, string original, out LyricDrawScriptDiagnostic? diagnostic)
    {
        var statements = new List<Statement>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < cleaned.Length; index++)
        {
            var current = cleaned[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '\'' or '"') quote = current;
            else if (current == '(') depth++;
            else if (current == ')')
            {
                depth--;
                if (depth < 0)
                {
                    diagnostic = CreateDiagnostic(original, "存在多余的右括号。", index);
                    return [];
                }
            }
            else if ((current == ';' || current == '\n' || current == '\r') && depth == 0)
            {
                statements.Add(new Statement(cleaned[start..index], start));
                start = index + 1;
            }
        }

        if (quote != '\0')
        {
            diagnostic = CreateDiagnostic(original, "字符串没有结束引号。", Math.Max(cleaned.Length - 1, 0));
            return [];
        }

        if (depth != 0)
        {
            diagnostic = CreateDiagnostic(original, "括号没有闭合。", Math.Max(cleaned.Length - 1, 0));
            return [];
        }

        if (start < cleaned.Length) statements.Add(new Statement(cleaned[start..], start));
        diagnostic = null;
        return statements.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToList();
    }

    private static IReadOnlyList<string> SplitArguments(
        string source,
        string original,
        int originalOffset,
        out LyricDrawScriptDiagnostic? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostic = null;
            return [];
        }

        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '\'' or '"') quote = current;
            else if (current == '(') depth++;
            else if (current == ')') depth--;
            else if (current == ',' && depth == 0)
            {
                var argument = source[start..index].Trim();
                if (argument.Length == 0)
                {
                    diagnostic = CreateDiagnostic(original, "绘图命令包含空参数。", originalOffset + index);
                    return [];
                }

                arguments.Add(argument);
                start = index + 1;
            }
        }

        var last = source[start..].Trim();
        if (last.Length == 0)
        {
            diagnostic = CreateDiagnostic(original, "绘图命令包含空参数。", originalOffset + source.Length);
            return [];
        }

        arguments.Add(last);
        diagnostic = null;
        return arguments;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static LyricDrawScriptParseResult Failure(string source, string message, int position) =>
        LyricDrawScriptParseResult.Failure(CreateDiagnostic(source, message, position));

    private static LyricDrawScriptDiagnostic CreateDiagnostic(string source, string message, int position)
    {
        var (line, column) = GetLineColumn(source, position);
        return new LyricDrawScriptDiagnostic(message, Math.Clamp(position, 0, source.Length), line, column);
    }

    private static (int Line, int Column) GetLineColumn(string source, int position)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < Math.Clamp(position, 0, source.Length); index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else column++;
        }

        return (line, column);
    }

    private readonly record struct Statement(string Text, int Position);
}
