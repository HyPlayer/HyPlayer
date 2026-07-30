#nullable enable

using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class FocusedTextOperationDescriptor
{
    public required string TypeId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<LyricOperationParameterDescriptor> Parameters { get; init; }
    public bool SupportsScript { get; init; }
    public bool IsRequired { get; init; }
    public bool SupportsTargets { get; init; } = true;
}

public sealed class CompiledFocusedTextEffectProfile
{
    internal CompiledFocusedTextEffectProfile(
        FocusedTextEffectDefinition definition,
        IReadOnlyList<CompiledFocusedTextOperation> operations)
    {
        Definition = definition;
        Operations = operations;
    }

    public FocusedTextEffectDefinition Definition { get; }
    internal IReadOnlyList<CompiledFocusedTextOperation> Operations { get; }
}

internal sealed class CompiledFocusedTextOperation
{
    public required FocusedTextOperationDefinition Definition { get; init; }
    public required IReadOnlySet<string> Targets { get; init; }
    public required IReadOnlyDictionary<string, CompiledFocusedScalarParameter> Scalars { get; init; }
    public required IReadOnlyDictionary<string, CompiledFocusedColorParameter> Colors { get; init; }
    public CompiledFocusedDrawScript? DrawScript { get; init; }
}

internal sealed record CompiledFocusedTransition(
    FocusedTextScalarExpression Duration,
    string EasingId,
    string Mode,
    IReadOnlyDictionary<string, FocusedTextScalarExpression> Arguments);

internal sealed record CompiledFocusedScalarParameter(
    FocusedTextScalarExpression Expression,
    CompiledFocusedTransition? Transition,
    float? Minimum,
    float? Maximum);

internal sealed record CompiledFocusedColorParameter(
    FocusedTextColorExpression Expression,
    CompiledFocusedTransition? Transition);

internal enum FocusedDrawScriptPlacement
{
    BehindGlyph,
    AboveGlyph
}

internal sealed class CompiledFocusedDrawScript
{
    public required FocusedDrawScriptPlacement Placement { get; init; }
    public required IReadOnlyList<CompiledFocusedDrawCommand> Commands { get; init; }
}

internal sealed class CompiledFocusedDrawCommand
{
    private readonly ILyricDrawCommandFactory _factory;
    private readonly IReadOnlyList<CompiledFocusedDrawArgument> _arguments;

    public CompiledFocusedDrawCommand(
        ILyricDrawCommandFactory factory,
        IReadOnlyList<CompiledFocusedDrawArgument> arguments)
    {
        _factory = factory;
        _arguments = arguments;
    }

    public void Execute(
        LyricDrawExecutionContext context,
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph)
    {
        var values = _arguments
            .Select(argument => argument.Evaluate(line, frame, text, word, glyph))
            .ToArray();
        _factory.Execute(context, values);
    }
}

internal sealed class CompiledFocusedDrawArgument
{
    private readonly FocusedTextScalarExpression? _scalar;
    private readonly FocusedTextColorExpression? _color;
    private readonly FocusedTextTextExpression? _text;

    private CompiledFocusedDrawArgument(
        LyricExpressionValueType type,
        FocusedTextScalarExpression? scalar,
        FocusedTextColorExpression? color,
        FocusedTextTextExpression? text)
    {
        Type = type;
        _scalar = scalar;
        _color = color;
        _text = text;
    }

    public LyricExpressionValueType Type { get; }

    public static CompiledFocusedDrawArgument Scalar(FocusedTextScalarExpression expression) =>
        new(LyricExpressionValueType.Scalar, expression, null, null);

    public static CompiledFocusedDrawArgument Color(FocusedTextColorExpression expression) =>
        new(LyricExpressionValueType.Color, null, expression, null);

    public static CompiledFocusedDrawArgument Text(FocusedTextTextExpression expression) =>
        new(LyricExpressionValueType.Text, null, null, expression);

    public LyricDrawValue Evaluate(
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph)
    {
        var fx = LyricExpressionFunctions.Instance;
        return Type switch
        {
            LyricExpressionValueType.Scalar => EvaluateScalar(line, frame, text, word, glyph, fx),
            LyricExpressionValueType.Color => LyricDrawValue.FromColor(_color!(line, frame, text, word, glyph, fx)),
            LyricExpressionValueType.Text => LyricDrawValue.FromText(_text!(line, frame, text, word, glyph, fx) ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private LyricDrawValue EvaluateScalar(
        LyricExpressionLine line,
        LyricExpressionFrame frame,
        FocusedTextExpressionText text,
        FocusedTextExpressionWord word,
        FocusedTextExpressionGlyph glyph,
        LyricExpressionFunctions fx)
    {
        var value = _scalar!(line, frame, text, word, glyph, fx);
        if (!float.IsFinite(value)) throw new InvalidOperationException("绘图参数返回了 NaN 或 Infinity。");
        return LyricDrawValue.FromScalar(value);
    }
}

internal sealed class FocusedTextEffectCompiler
{
    private readonly ILyricExpressionCompiler _expressions;
    private readonly ILyricDrawScriptParser _drawScriptParser;
    private readonly LyricDrawCommandRegistry _drawCommands;

    public FocusedTextEffectCompiler(
        ILyricExpressionCompiler expressions,
        ILyricDrawScriptParser drawScriptParser,
        LyricDrawCommandRegistry drawCommands)
    {
        _expressions = expressions;
        _drawScriptParser = drawScriptParser;
        _drawCommands = drawCommands;
    }

    public static IReadOnlyList<FocusedTextOperationDescriptor> Descriptors { get; } = (FocusedTextOperationDescriptor[])
    [
        new FocusedTextOperationDescriptor
        {
            TypeId = FocusedTextBuiltInOperationTypes.HighlightReveal,
            DisplayName = "高亮推进",
            Description = "把当前 Word 的已高亮与未高亮贡献按配置互补展开。",
            Parameters = (LyricOperationParameterDescriptor[])
            [
                Scalar("revealTimeOffsetMs", "推进时间偏移", "0"),
                Scalar("featherDip", "羽化宽度", "0")
            ],
            IsRequired = true,
            SupportsTargets = false
        },
        Descriptor(FocusedTextBuiltInOperationTypes.Color, "颜色", "设置所选文本贡献的颜色。",
            Color("color", "颜色", "line.FocusingColor")),
        Descriptor(FocusedTextBuiltInOperationTypes.Opacity, "透明度", "乘算所选文本贡献的透明度。",
            Scalar("opacity", "透明度", "1", 0, 1)),
        Descriptor(FocusedTextBuiltInOperationTypes.Transform2D, "逐字 2D 变换", "对每个 GlyphUnit 应用位移、缩放和旋转。",
            Scalar("x", "X 位移", "0"), Scalar("y", "Y 位移", "0"),
            Scalar("scaleX", "X 缩放", "1", -10, 10), Scalar("scaleY", "Y 缩放", "1", -10, 10),
            Scalar("rotation", "旋转角度", "0"), Scalar("anchorX", "X 锚点", "0"), Scalar("anchorY", "Y 锚点", "0")),
        Descriptor(FocusedTextBuiltInOperationTypes.Transform3D, "逐字 3D 变换", "对每个 GlyphUnit 应用三轴旋转与透视深度。",
            Scalar("angleX", "X 角度", "0"), Scalar("angleY", "Y 角度", "0"),
            Scalar("angleZ", "Z 角度", "0"), Scalar("depth", "景深", "3000", 1, 100000),
            Scalar("anchorX", "X 锚点", "0"), Scalar("anchorY", "Y 锚点", "0")),
        Descriptor(FocusedTextBuiltInOperationTypes.GaussianBlur, "逐字模糊", "模糊所选 GlyphUnit。",
            Scalar("amount", "模糊量", "0", 0, 250)),
        Descriptor(FocusedTextBuiltInOperationTypes.Glow, "逐字辉光", "在所选 GlyphUnit 后绘制辉光。",
            Scalar("x", "X 偏移", "0"), Scalar("y", "Y 偏移", "0"),
            Scalar("blur", "辉光半径", "4", 0, 250), Scalar("opacity", "辉光透明度", "0.4", 0, 1),
            Color("color", "辉光颜色", "line.FocusingColor")),
        Descriptor(FocusedTextBuiltInOperationTypes.Stroke, "逐字描边", "为所选 GlyphUnit 绘制描边。",
            Scalar("width", "描边宽度", "1", 0, 8), Color("color", "描边颜色", "line.FocusingColor")),
        Descriptor(FocusedTextBuiltInOperationTypes.GlyphLift, "Glyph 抬升", "使用当前 GlyphLift 节点的 glyph.LiftProgress 抬升 GlyphUnit 或 Word。",
            Scalar("height", "抬升高度", "3"), Scalar("overlap", "Glyph 黏连度", "0", 0, 1),
            Scalar("wholeWordThresholdMs", "整词阈值", "1000"),
            Scalar("liftTimeOffsetMs", "抬升时间偏移", "0"), Scalar("liftFinishDurationMs", "结束延长", "0"),
            Scalar("exponent", "指数", "2"), Scalar("springiness", "弹性强度", "3"),
            Scalar("oscillations", "振荡次数", "3"), Scalar("bounces", "回弹次数", "2"),
            Scalar("bounciness", "回弹强度", "2")),
        new FocusedTextOperationDescriptor
        {
            TypeId = FocusedTextBuiltInOperationTypes.DrawScript,
            DisplayName = "Glyph 绘图脚本",
            Description = "在 GlyphUnit 局部坐标中执行受限绘图脚本。",
            Parameters = Array.Empty<LyricOperationParameterDescriptor>(),
            SupportsScript = true
        }
    ];

    public CompiledFocusedTextEffectProfile? Compile(
        FocusedTextEffectDefinition definition,
        List<LyricProfileDiagnostic> diagnostics)
    {
        var descriptors = Descriptors.ToDictionary(item => item.TypeId, StringComparer.OrdinalIgnoreCase);
        var operations = new List<CompiledFocusedTextOperation>();
        foreach (var operation in definition.Operations.Take(LyricEffectProfileValidation.MaximumOperationCount))
        {
            if (!descriptors.TryGetValue(operation.TypeId, out var descriptor))
            {
                diagnostics.Add(new LyricProfileDiagnostic(
                    LyricProfileDiagnosticSeverity.Warning,
                    $"当前版本未安装聚焦节点类型“{operation.TypeId}”，该节点会保留但不参与渲染。",
                    operation.InstanceId));
                continue;
            }

            var scalars = new Dictionary<string, CompiledFocusedScalarParameter>(StringComparer.Ordinal);
            var colors = new Dictionary<string, CompiledFocusedColorParameter>(StringComparer.Ordinal);
            CompiledFocusedDrawScript? drawScript = null;
            foreach (var parameterDescriptor in descriptor.Parameters)
            {
                var source = operation.Parameters.TryGetValue(parameterDescriptor.Key, out var parameter)
                    ? parameter.Expression
                    : parameterDescriptor.DefaultExpression;
                if (parameterDescriptor.ValueType == LyricExpressionValueType.Color)
                {
                    var result = _expressions.CompileFocusedColor(source);
                    if (result.IsSuccess)
                        colors[parameterDescriptor.Key] = new CompiledFocusedColorParameter(
                            result.Expression!, CompileTransition(operation, parameterDescriptor.Key, parameter?.Transition, diagnostics));
                    else diagnostics.Add(ToDiagnostic(operation, parameterDescriptor.Key, result.Diagnostic!));
                }
                else
                {
                    var result = _expressions.CompileFocusedScalar(source);
                    if (result.IsSuccess)
                        scalars[parameterDescriptor.Key] = new CompiledFocusedScalarParameter(
                            result.Expression!, CompileTransition(operation, parameterDescriptor.Key, parameter?.Transition, diagnostics),
                            parameterDescriptor.Minimum, parameterDescriptor.Maximum);
                    else diagnostics.Add(ToDiagnostic(operation, parameterDescriptor.Key, result.Diagnostic!));
                }
            }

            if (descriptor.SupportsScript)
                drawScript = CompileDrawScript(operation, diagnostics);

            if (diagnostics.Any(item => item.InstanceId == operation.InstanceId && item.Severity == LyricProfileDiagnosticSeverity.Error))
            {
                for (var index = 0; index < diagnostics.Count; index++)
                {
                    if (diagnostics[index].InstanceId == operation.InstanceId &&
                        diagnostics[index].Severity == LyricProfileDiagnosticSeverity.Error)
                        diagnostics[index] = diagnostics[index] with
                        {
                            Severity = LyricProfileDiagnosticSeverity.Warning,
                            Message = $"节点已跳过：{diagnostics[index].Message}"
                        };
                }
                continue;
            }
            if (!operation.IsEnabled) continue;
            operations.Add(new CompiledFocusedTextOperation
            {
                Definition = LyricEffectPresets.CloneFocusedOperation(operation, renewInstanceId: false),
                Targets = operation.Targets.ToHashSet(StringComparer.Ordinal),
                Scalars = scalars,
                Colors = colors,
                DrawScript = drawScript
            });
        }

        return new CompiledFocusedTextEffectProfile(
            LyricEffectPresets.CloneFocusedText(definition), operations);
    }

    private CompiledFocusedDrawScript? CompileDrawScript(
        FocusedTextOperationDefinition operation,
        ICollection<LyricProfileDiagnostic> diagnostics)
    {
        var parseResult = _drawScriptParser.Parse(operation.Script ?? string.Empty);
        if (!parseResult.IsSuccess)
        {
            diagnostics.Add(new LyricProfileDiagnostic(
                LyricProfileDiagnosticSeverity.Error,
                parseResult.Diagnostic!.Message,
                operation.InstanceId,
                "script",
                parseResult.Diagnostic.Line,
                parseResult.Diagnostic.Column));
            return null;
        }

        var commands = new List<CompiledFocusedDrawCommand>();
        var saveDepth = 0;
        foreach (var command in parseResult.Commands)
        {
            if (!_drawCommands.TryGet(command.Name, out var factory))
            {
                diagnostics.Add(ScriptDiagnostic(operation, command, $"未知绘图命令“{command.Name}”。"));
                continue;
            }
            if (factory.Signature.Arguments.Count != command.Arguments.Count)
            {
                diagnostics.Add(ScriptDiagnostic(
                    operation,
                    command,
                    $"命令 {command.Name} 需要 {factory.Signature.Arguments.Count} 个参数，实际为 {command.Arguments.Count} 个。"));
                continue;
            }

            var arguments = new List<CompiledFocusedDrawArgument>();
            for (var index = 0; index < command.Arguments.Count; index++)
            {
                var argument = CompileDrawArgument(
                    operation,
                    command,
                    command.Arguments[index],
                    factory.Signature.Arguments[index],
                    diagnostics);
                if (argument is not null) arguments.Add(argument);
            }
            if (arguments.Count == command.Arguments.Count)
                commands.Add(new CompiledFocusedDrawCommand(factory, arguments));

            if (command.Name.Equals("Save", StringComparison.OrdinalIgnoreCase)) saveDepth++;
            else if (command.Name.Equals("Restore", StringComparison.OrdinalIgnoreCase) && --saveDepth < 0)
            {
                diagnostics.Add(ScriptDiagnostic(operation, command, "Restore 没有匹配的 Save。"));
                saveDepth = 0;
            }
        }

        if (saveDepth != 0)
            diagnostics.Add(new LyricProfileDiagnostic(
                LyricProfileDiagnosticSeverity.Error,
                "绘图脚本中的 Save/Restore 不平衡。",
                operation.InstanceId,
                "script"));

        if (diagnostics.Any(item =>
                item.InstanceId == operation.InstanceId &&
                item.Parameter == "script" &&
                item.Severity == LyricProfileDiagnosticSeverity.Error))
            return null;

        var placement = operation.Options.TryGetValue("placement", out var value) &&
                        value.Equals("BehindGlyph", StringComparison.OrdinalIgnoreCase)
            ? FocusedDrawScriptPlacement.BehindGlyph
            : FocusedDrawScriptPlacement.AboveGlyph;
        return new CompiledFocusedDrawScript { Placement = placement, Commands = commands };
    }

    private CompiledFocusedTransition? CompileTransition(
        FocusedTextOperationDefinition operation,
        string parameter,
        LyricTransitionDefinition? transition,
        ICollection<LyricProfileDiagnostic> diagnostics)
    {
        if (transition is null) return null;
        var duration = _expressions.CompileFocusedScalar(transition.DurationMs);
        if (!duration.IsSuccess)
        {
            diagnostics.Add(ToDiagnostic(operation, $"{parameter}.transition.durationMs", duration.Diagnostic!));
            return null;
        }

        var arguments = new Dictionary<string, FocusedTextScalarExpression>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, source) in transition.Arguments)
        {
            var result = _expressions.CompileFocusedScalar(source);
            if (result.IsSuccess) arguments[key] = result.Expression!;
            else diagnostics.Add(ToDiagnostic(operation, $"{parameter}.transition.arguments.{key}", result.Diagnostic!));
        }
        return new CompiledFocusedTransition(duration.Expression!, transition.EasingId, transition.Mode, arguments);
    }

    private CompiledFocusedDrawArgument? CompileDrawArgument(
        FocusedTextOperationDefinition operation,
        LyricDrawScriptCommand command,
        string source,
        LyricExpressionValueType type,
        ICollection<LyricProfileDiagnostic> diagnostics)
    {
        switch (type)
        {
            case LyricExpressionValueType.Scalar:
            {
                var result = _expressions.CompileFocusedScalar(source);
                if (result.IsSuccess) return CompiledFocusedDrawArgument.Scalar(result.Expression!);
                diagnostics.Add(ExpressionScriptDiagnostic(operation, command, result.Diagnostic!));
                return null;
            }
            case LyricExpressionValueType.Color:
            {
                var result = _expressions.CompileFocusedColor(source);
                if (result.IsSuccess) return CompiledFocusedDrawArgument.Color(result.Expression!);
                diagnostics.Add(ExpressionScriptDiagnostic(operation, command, result.Diagnostic!));
                return null;
            }
            case LyricExpressionValueType.Text:
            {
                var result = _expressions.CompileFocusedText(source);
                if (result.IsSuccess) return CompiledFocusedDrawArgument.Text(result.Expression!);
                diagnostics.Add(ExpressionScriptDiagnostic(operation, command, result.Diagnostic!));
                return null;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    private static LyricProfileDiagnostic ScriptDiagnostic(
        FocusedTextOperationDefinition operation,
        LyricDrawScriptCommand command,
        string message) => new(
        LyricProfileDiagnosticSeverity.Error,
        message,
        operation.InstanceId,
        "script",
        command.Line,
        1);

    private static LyricProfileDiagnostic ExpressionScriptDiagnostic(
        FocusedTextOperationDefinition operation,
        LyricDrawScriptCommand command,
        LyricExpressionDiagnostic diagnostic) => new(
        LyricProfileDiagnosticSeverity.Error,
        diagnostic.Message,
        operation.InstanceId,
        "script",
        command.Line + diagnostic.Line - 1,
        diagnostic.Column);

    private static LyricProfileDiagnostic ToDiagnostic(
        FocusedTextOperationDefinition operation,
        string parameter,
        LyricExpressionDiagnostic diagnostic) => new(
            LyricProfileDiagnosticSeverity.Error,
            diagnostic.Message,
            operation.InstanceId,
            parameter,
            diagnostic.Line,
            diagnostic.Column);

    private static FocusedTextOperationDescriptor Descriptor(
        string typeId,
        string displayName,
        string description,
        params LyricOperationParameterDescriptor[] parameters) => new()
    {
        TypeId = typeId,
        DisplayName = displayName,
        Description = description,
        Parameters = parameters
    };

    private static LyricOperationParameterDescriptor Scalar(
        string key, string name, string expression, float? minimum = null, float? maximum = null) => new()
    {
        Key = key,
        DisplayName = name,
        ValueType = LyricExpressionValueType.Scalar,
        DefaultExpression = expression,
        SupportsTransition = true,
        Minimum = minimum,
        Maximum = maximum
    };

    private static LyricOperationParameterDescriptor Color(string key, string name, string expression) => new()
    {
        Key = key,
        DisplayName = name,
        ValueType = LyricExpressionValueType.Color,
        DefaultExpression = expression,
        SupportsTransition = true
    };
}
