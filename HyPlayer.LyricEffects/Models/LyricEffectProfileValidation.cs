using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Presets;

namespace HyPlayer.LyricEffects.Models;

public sealed record LyricEffectProfileValidationError(
    string Message,
    string? InstanceId = null,
    string? Property = null);

/// <summary>
/// 与具体 UI、存储和渲染后端无关的 .hylfx 格式约束与显式版本迁移入口。
/// </summary>
public static class LyricEffectProfileValidation
{
    public const int MaximumFileBytes = 1024 * 1024;
    public const int MaximumOperationCount = 64;

    public static IReadOnlyList<LyricEffectProfileValidationError> Validate(LyricEffectProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<LyricEffectProfileValidationError>();

        if (!string.Equals(document.Format, LyricEffectProfileDocument.CurrentFormat, StringComparison.Ordinal))
            errors.Add(new("不是有效的 HyPlayer 歌词特效文件。"));
        if (document.SchemaVersion != LyricEffectProfileDocument.CurrentSchemaVersion)
            errors.Add(new($"仅支持 schemaVersion {LyricEffectProfileDocument.CurrentSchemaVersion}，当前为 {document.SchemaVersion}。"));
        if (document.ExpressionApiVersion != LyricEffectProfileDocument.CurrentExpressionApiVersion)
            errors.Add(new($"仅支持 expressionApiVersion {LyricEffectProfileDocument.CurrentExpressionApiVersion}，当前为 {document.ExpressionApiVersion}。"));
        if (document.Operations.Count > MaximumOperationCount)
            errors.Add(new($"歌词特效链最多允许 {MaximumOperationCount} 个节点。"));
        if (document.FocusedText.Operations.Count > MaximumOperationCount)
            errors.Add(new($"聚焦歌词特效链最多允许 {MaximumOperationCount} 个节点。"));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in document.Operations.Take(MaximumOperationCount))
        {
            if (string.IsNullOrWhiteSpace(operation.InstanceId) || !ids.Add(operation.InstanceId))
                errors.Add(new("节点 instanceId 不能为空或重复。", operation.InstanceId));
            if (string.IsNullOrWhiteSpace(operation.TypeId))
                errors.Add(new("节点 typeId 不能为空。", operation.InstanceId, "typeId"));
            if (operation.Script?.Length > LyricDrawScriptParser.MaximumScriptLength)
                errors.Add(new($"绘图脚本不能超过 {LyricDrawScriptParser.MaximumScriptLength} 个字符。", operation.InstanceId, "script"));

            foreach (var (key, parameter) in operation.Parameters)
            {
                ValidateParameter(operation.InstanceId, key, parameter, errors);
            }
        }

        foreach (var operation in document.FocusedText.Operations.Take(MaximumOperationCount))
        {
            if (string.IsNullOrWhiteSpace(operation.InstanceId) || !ids.Add(operation.InstanceId))
                errors.Add(new("节点 instanceId 不能为空或重复。", operation.InstanceId));
            if (string.IsNullOrWhiteSpace(operation.TypeId))
                errors.Add(new("节点 typeId 不能为空。", operation.InstanceId, "typeId"));
            if (operation.TypeId != FocusedTextBuiltInOperationTypes.HighlightReveal && operation.Targets.Count == 0)
                errors.Add(new("聚焦歌词节点必须至少选择一个目标。", operation.InstanceId, "targets"));
            if (operation.Targets.Distinct(StringComparer.Ordinal).Count() != operation.Targets.Count)
                errors.Add(new("聚焦歌词节点不能包含重复目标。", operation.InstanceId, "targets"));
            if (operation.Script?.Length > LyricDrawScriptParser.MaximumScriptLength)
                errors.Add(new($"绘图脚本不能超过 {LyricDrawScriptParser.MaximumScriptLength} 个字符。", operation.InstanceId, "script"));

            foreach (var (key, parameter) in operation.Parameters)
            {
                ValidateParameter(operation.InstanceId, key, parameter, errors);
            }
        }

        ValidateRequiredOperation(document, LyricBuiltInOperationTypes.Source, "歌词内容", errors);
        ValidateRequiredOperation(document, LyricBuiltInOperationTypes.Debug, "Debug 信息", errors);
        ValidateRequiredFocusedOperation(document, FocusedTextBuiltInOperationTypes.HighlightReveal, "高亮推进", errors);

        return errors;
    }

    public static LyricEffectProfileDocument MigrateToCurrent(LyricEffectProfileDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SchemaVersion != LyricEffectProfileDocument.CurrentSchemaVersion ||
            source.ExpressionApiVersion != LyricEffectProfileDocument.CurrentExpressionApiVersion)
            throw new NotSupportedException(
                $"仅支持 schema v{LyricEffectProfileDocument.CurrentSchemaVersion} / Expression API v{LyricEffectProfileDocument.CurrentExpressionApiVersion}，不提供旧版本迁移。");

        return LyricEffectPresets.CloneProfile(source);
    }

    private static void ValidateRequiredOperation(
        LyricEffectProfileDocument document,
        string typeId,
        string displayName,
        ICollection<LyricEffectProfileValidationError> errors)
    {
        var matches = document.Operations.Where(item => item.TypeId == typeId).ToList();
        if (matches.Count != 1)
        {
            errors.Add(new($"配置必须包含且只能包含一个“{displayName}”节点。"));
            return;
        }

        if (!matches[0].IsEnabled)
            errors.Add(new($"必需节点“{displayName}”不能被禁用。", matches[0].InstanceId, "isEnabled"));
    }

    private static void ValidateRequiredFocusedOperation(
        LyricEffectProfileDocument document,
        string typeId,
        string displayName,
        ICollection<LyricEffectProfileValidationError> errors)
    {
        var matches = document.FocusedText.Operations.Where(item => item.TypeId == typeId).ToList();
        if (matches.Count != 1)
        {
            errors.Add(new($"聚焦歌词配置必须包含且只能包含一个“{displayName}”节点。"));
            return;
        }

        if (!matches[0].IsEnabled)
            errors.Add(new($"必需节点“{displayName}”不能被禁用。", matches[0].InstanceId, "isEnabled"));
    }

    private static void ValidateParameter(
        string instanceId,
        string key,
        LyricOperationParameterDefinition parameter,
        ICollection<LyricEffectProfileValidationError> errors)
    {
        if (parameter.Expression.Length > LyricExpressionCompiler.MaximumExpressionLength)
            errors.Add(new($"表达式不能超过 {LyricExpressionCompiler.MaximumExpressionLength} 个字符。", instanceId, key));
        if (parameter.Transition is not { } transition) return;
        if (transition.DurationMs.Length > LyricExpressionCompiler.MaximumExpressionLength)
            errors.Add(new($"缓动时长表达式不能超过 {LyricExpressionCompiler.MaximumExpressionLength} 个字符。", instanceId, $"{key}.transition.durationMs"));
        foreach (var (argument, expression) in transition.Arguments)
        {
            if (expression.Length > LyricExpressionCompiler.MaximumExpressionLength)
                errors.Add(new($"缓动曲线参数表达式不能超过 {LyricExpressionCompiler.MaximumExpressionLength} 个字符。", instanceId, $"{key}.transition.arguments.{argument}"));
        }
    }

}
