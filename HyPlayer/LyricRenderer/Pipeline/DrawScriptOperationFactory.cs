using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.UI;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;

namespace HyPlayer.LyricRenderer.Pipeline;

public sealed class LyricDrawCommandRegistry
{
    private readonly Dictionary<string, ILyricDrawCommandFactory> _factories =
        [with(StringComparer.OrdinalIgnoreCase)];

    public LyricDrawCommandRegistry()
    {
        foreach (var factory in BuiltInDrawCommandFactories.CreateAll()) Register(factory);
    }

    public IReadOnlyList<LyricDrawCommandSignature> Signatures =>
        (LyricDrawCommandSignature[])
        [.. _factories.Values.Select(factory => factory.Signature).OrderBy(signature => signature.Name)];

    public void Register(ILyricDrawCommandFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd(factory.Signature.Name, factory))
            throw new InvalidOperationException($"绘图命令“{factory.Signature.Name}”已注册。");
    }

    internal bool TryGet(string name, out ILyricDrawCommandFactory factory)
    {
        return _factories.TryGetValue(name, out factory!);
    }
}

internal sealed partial class DrawScriptOperationFactory(
    ILyricExpressionCompiler expressionCompiler,
    ILyricDrawScriptParser parser,
    LyricDrawCommandRegistry commands) : ILyricRenderOperationFactory
{
    private readonly LyricDrawCommandRegistry _commands = commands;

    public LyricRenderOperationDescriptor Descriptor { get; } = new()
    {
        TypeId = LyricBuiltInOperationTypes.DrawScript,
        DisplayName = "绘图脚本",
        Description = "使用安全绘图命令在当前歌词图像前方或后方绘制。",
        Parameters = [],
        SupportsScript = true,
        Category = LyricRenderOperationCategory.Draw
    };

    public LyricOperationCompileResult Compile(LyricRenderOperationDefinition definition)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var parseResult = parser.Parse(definition.Script ?? string.Empty);
        if (!parseResult.IsSuccess)
        {
            diagnostics.Add(new LyricProfileDiagnostic(
                LyricProfileDiagnosticSeverity.Error,
                parseResult.Diagnostic!.Message,
                definition.InstanceId,
                "script",
                parseResult.Diagnostic.Line,
                parseResult.Diagnostic.Column));
            return new LyricOperationCompileResult { Diagnostics = diagnostics };
        }

        var commands = new List<CompiledDrawCommand>();
        var saveDepth = 0;
        foreach (var command in parseResult.Commands)
        {
            if (!_commands.TryGet(command.Name, out var factory))
            {
                diagnostics.Add(new LyricProfileDiagnostic(
                    LyricProfileDiagnosticSeverity.Error,
                    $"未知绘图命令“{command.Name}”。",
                    definition.InstanceId,
                    "script",
                    command.Line,
                    1));
                continue;
            }

            if (factory.Signature.Arguments.Count != command.Arguments.Count)
            {
                diagnostics.Add(new LyricProfileDiagnostic(
                    LyricProfileDiagnosticSeverity.Error,
                    $"命令 {command.Name} 需要 {factory.Signature.Arguments.Count} 个参数，实际为 {command.Arguments.Count} 个。",
                    definition.InstanceId,
                    "script",
                    command.Line,
                    1));
                continue;
            }

            var arguments = new List<CompiledDrawArgument>();
            for (var index = 0; index < command.Arguments.Count; index++)
            {
                var argument = CompileArgument(
                    definition,
                    command,
                    command.Arguments[index],
                    factory.Signature.Arguments[index],
                    diagnostics);
                if (argument is not null) arguments.Add(argument);
            }

            if (arguments.Count == command.Arguments.Count)
                commands.Add(new CompiledDrawCommand(factory, arguments));

            if (command.Name.Equals("Save", StringComparison.OrdinalIgnoreCase))
            {
                saveDepth++;
            }
            else if (command.Name.Equals("Restore", StringComparison.OrdinalIgnoreCase))
            {
                saveDepth--;
                if (saveDepth < 0)
                {
                    diagnostics.Add(new LyricProfileDiagnostic(
                        LyricProfileDiagnosticSeverity.Error,
                        "Restore 没有匹配的 Save。",
                        definition.InstanceId,
                        "script",
                        command.Line,
                        1));
                    saveDepth = 0;
                }
            }
        }

        if (saveDepth != 0)
            diagnostics.Add(new LyricProfileDiagnostic(
                LyricProfileDiagnosticSeverity.Error,
                "绘图脚本中的 Save/Restore 不平衡。",
                definition.InstanceId,
                "script"));

        if (diagnostics.Any(item => item.Severity == LyricProfileDiagnosticSeverity.Error))
            return new LyricOperationCompileResult { Diagnostics = diagnostics };

        var placement = definition.Options.TryGetValue("placement", out var value) &&
                        value.Equals("BehindSource", StringComparison.OrdinalIgnoreCase)
            ? DrawScriptPlacement.BehindSource
            : DrawScriptPlacement.AboveSource;
        return new LyricOperationCompileResult
        {
            Diagnostics = diagnostics,
            Operation = new CompiledLyricRenderOperation
            {
                Definition = definition,
                Create = () => new DrawScriptOperation(commands, placement)
            }
        };
    }

    private CompiledDrawArgument? CompileArgument(
        LyricRenderOperationDefinition definition,
        LyricDrawScriptCommand command,
        string source,
        LyricExpressionValueType valueType,
        List<LyricProfileDiagnostic> diagnostics)
    {
        switch (valueType)
        {
            case LyricExpressionValueType.Scalar:
            {
                var result = expressionCompiler.CompileScalar(source);
                if (result.IsSuccess) return CompiledDrawArgument.Scalar(result.Expression!);
                AddExpressionDiagnostic(definition, command, result.Diagnostic!, diagnostics);
                return null;
            }
            case LyricExpressionValueType.Color:
            {
                var result = expressionCompiler.CompileColor(source);
                if (result.IsSuccess) return CompiledDrawArgument.Color(result.Expression!);
                AddExpressionDiagnostic(definition, command, result.Diagnostic!, diagnostics);
                return null;
            }
            case LyricExpressionValueType.Text:
            {
                var result = expressionCompiler.CompileText(source);
                if (result.IsSuccess) return CompiledDrawArgument.Text(result.Expression!);
                AddExpressionDiagnostic(definition, command, result.Diagnostic!, diagnostics);
                return null;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(valueType));
        }
    }

    private static void AddExpressionDiagnostic(
        LyricRenderOperationDefinition definition,
        LyricDrawScriptCommand command,
        LyricExpressionDiagnostic diagnostic,
        List<LyricProfileDiagnostic> diagnostics)
    {
        diagnostics.Add(new LyricProfileDiagnostic(
            LyricProfileDiagnosticSeverity.Error,
            diagnostic.Message,
            definition.InstanceId,
            "script",
            command.Line + diagnostic.Line - 1,
            diagnostic.Column));
    }

    private enum DrawScriptPlacement
    {
        BehindSource,
        AboveSource
    }

    private sealed partial class DrawScriptOperation(
        IReadOnlyList<CompiledDrawCommand> commands,
        DrawScriptPlacement placement) : ILyricRenderOperation
    {
        private readonly IReadOnlyList<CompiledDrawCommand> _commands = commands;

        public ICanvasImage Apply(ICanvasImage source, LyricRenderOperationContext context)
        {
            var commandList = context.Resources.Track(new CanvasCommandList(context.TargetSession));
            using var session = commandList.CreateDrawingSession();
            var executionContext = new LyricDrawExecutionContext(session);
            if (placement == DrawScriptPlacement.AboveSource) session.DrawImage(source);
            foreach (var command in _commands) command.Execute(executionContext, context);
            executionContext.EnsureBalanced();
            if (placement == DrawScriptPlacement.BehindSource) session.DrawImage(source);
            return commandList;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CompiledDrawCommand(
        ILyricDrawCommandFactory factory,
        IReadOnlyList<CompiledDrawArgument> arguments)
    {
        public void Execute(LyricDrawExecutionContext executionContext, LyricRenderOperationContext renderContext)
        {
            var values = arguments.Select(argument => argument.Evaluate(renderContext)).ToArray();
            factory.Execute(executionContext, values);
        }
    }

    private sealed class CompiledDrawArgument
    {
        private readonly LyricColorExpression? _color;
        private readonly LyricScalarExpression? _scalar;
        private readonly LyricTextExpression? _text;

        private CompiledDrawArgument(
            LyricExpressionValueType type,
            LyricScalarExpression? scalar,
            LyricColorExpression? color,
            LyricTextExpression? text)
        {
            Type = type;
            _scalar = scalar;
            _color = color;
            _text = text;
        }

        public LyricExpressionValueType Type { get; }

        public static CompiledDrawArgument Scalar(LyricScalarExpression expression)
        {
            return new CompiledDrawArgument(LyricExpressionValueType.Scalar, expression, null, null);
        }

        public static CompiledDrawArgument Color(LyricColorExpression expression)
        {
            return new CompiledDrawArgument(LyricExpressionValueType.Color, null, expression, null);
        }

        public static CompiledDrawArgument Text(LyricTextExpression expression)
        {
            return new CompiledDrawArgument(LyricExpressionValueType.Text, null, null, expression);
        }

        public LyricDrawValue Evaluate(LyricRenderOperationContext context)
        {
            return Type switch
            {
                LyricExpressionValueType.Scalar => EvaluateScalar(context),
                LyricExpressionValueType.Color => LyricDrawValue.FromColor(
                    _color!(context.Line, context.Frame, context.Functions)),
                LyricExpressionValueType.Text => LyricDrawValue.FromText(
                    _text!(context.Line, context.Frame, context.Functions) ?? string.Empty),
                _ => throw new ArgumentOutOfRangeException(nameof(context))
            };
        }

        private LyricDrawValue EvaluateScalar(LyricRenderOperationContext context)
        {
            var value = _scalar!(context.Line, context.Frame, context.Functions);
            if (!float.IsFinite(value)) throw new InvalidOperationException("绘图参数返回了 NaN 或 Infinity。");
            return LyricDrawValue.FromScalar(value);
        }
    }
}

internal static class BuiltInDrawCommandFactories
{
    private static readonly LyricExpressionValueType S = LyricExpressionValueType.Scalar;
    private static readonly LyricExpressionValueType C = LyricExpressionValueType.Color;
    private static readonly LyricExpressionValueType T = LyricExpressionValueType.Text;

    public static IReadOnlyList<ILyricDrawCommandFactory> CreateAll()
    {
        return (ILyricDrawCommandFactory[])
        [
            Command("FillRectangle", (LyricExpressionValueType[])[S, S, S, S, C], (context, value) =>
                context.Session.FillRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]))),
            Command("StrokeRectangle", (LyricExpressionValueType[])[S, S, S, S, C, S], (context, value) =>
                context.Session.DrawRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("FillRoundedRectangle", (LyricExpressionValueType[])[S, S, S, S, S, C], (context, value) =>
                context.Session.FillRoundedRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    value[4].Scalar, value[4].Scalar, Color(value[5]))),
            Command("StrokeRoundedRectangle", (LyricExpressionValueType[])[S, S, S, S, S, C, S], (context, value) =>
                context.Session.DrawRoundedRectangle(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    value[4].Scalar, value[4].Scalar, Color(value[5]), value[6].Scalar)),
            Command("FillEllipse", (LyricExpressionValueType[])[S, S, S, S, C], (context, value) =>
                context.Session.FillEllipse(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]))),
            Command("StrokeEllipse", (LyricExpressionValueType[])[S, S, S, S, C, S], (context, value) =>
                context.Session.DrawEllipse(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("DrawLine", (LyricExpressionValueType[])[S, S, S, S, C, S], (context, value) =>
                context.Session.DrawLine(value[0].Scalar, value[1].Scalar, value[2].Scalar, value[3].Scalar,
                    Color(value[4]), value[5].Scalar)),
            Command("DrawText", (LyricExpressionValueType[])[T, S, S, S, C], DrawText),
            Command("Save", [], (context, _) => context.Save()),
            Command("Restore", [], (context, _) => context.Restore()),
            Command("Translate", (LyricExpressionValueType[])[S, S], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateTranslation(value[0].Scalar, value[1].Scalar)),
            Command("Scale", (LyricExpressionValueType[])[S, S, S, S], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateScale(value[0].Scalar, value[1].Scalar,
                    new Vector2(value[2].Scalar, value[3].Scalar))),
            Command("Rotate", (LyricExpressionValueType[])[S, S, S], (context, value) =>
                context.Session.Transform *= Matrix3x2.CreateRotation(MathF.PI * value[0].Scalar / 180f,
                    new Vector2(value[1].Scalar, value[2].Scalar)))
        ];
    }

    private static DelegateDrawCommandFactory Command(
        string name,
        IReadOnlyList<LyricExpressionValueType> arguments,
        Action<LyricDrawExecutionContext, IReadOnlyList<LyricDrawValue>> execute)
    {
        return new DelegateDrawCommandFactory(new LyricDrawCommandSignature(name, arguments), execute);
    }

    private static void DrawText(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> values)
    {
        using var format = new CanvasTextFormat { FontSize = Math.Max(values[3].Scalar, 1) };
        context.Session.DrawText(values[0].Text ?? string.Empty, new Vector2(values[1].Scalar, values[2].Scalar),
            Color(values[4]), format);
    }

    private static Color Color(LyricDrawValue value)
    {
        return Windows.UI.Color.FromArgb(value.Color.A, value.Color.R, value.Color.G, value.Color.B);
    }

    private sealed class DelegateDrawCommandFactory(
        LyricDrawCommandSignature signature,
        Action<LyricDrawExecutionContext, IReadOnlyList<LyricDrawValue>> execute) : ILyricDrawCommandFactory
    {
        public LyricDrawCommandSignature Signature { get; } = signature;

        public void Execute(LyricDrawExecutionContext context, IReadOnlyList<LyricDrawValue> arguments)
        {
            execute(context, arguments);
        }
    }
}