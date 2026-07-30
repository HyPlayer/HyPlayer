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
        if (document.SchemaVersion > LyricEffectProfileDocument.CurrentSchemaVersion)
            errors.Add(new($"不支持 schemaVersion {document.SchemaVersion}。"));
        if (document.ExpressionApiVersion > LyricEffectProfileDocument.CurrentExpressionApiVersion)
            errors.Add(new($"不支持 expressionApiVersion {document.ExpressionApiVersion}。"));
        if (document.SchemaVersion < 0 || document.ExpressionApiVersion < 1)
            errors.Add(new("配置版本号无效。"));
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
                if (parameter.Expression.Length > LyricExpressionCompiler.MaximumExpressionLength)
                    errors.Add(new($"表达式不能超过 {LyricExpressionCompiler.MaximumExpressionLength} 个字符。", operation.InstanceId, key));
            }
        }

        foreach (var operation in document.FocusedText.Operations.Take(MaximumOperationCount))
        {
            if (string.IsNullOrWhiteSpace(operation.InstanceId) || !ids.Add(operation.InstanceId))
                errors.Add(new("节点 instanceId 不能为空或重复。", operation.InstanceId));
            if (string.IsNullOrWhiteSpace(operation.TypeId))
                errors.Add(new("节点 typeId 不能为空。", operation.InstanceId, "typeId"));
            if (operation.Targets.Count == 0)
                errors.Add(new("聚焦歌词节点必须至少选择一个目标。", operation.InstanceId, "targets"));
            if (operation.Targets.Distinct(StringComparer.Ordinal).Count() != operation.Targets.Count)
                errors.Add(new("聚焦歌词节点不能包含重复目标。", operation.InstanceId, "targets"));
            if (operation.Script?.Length > LyricDrawScriptParser.MaximumScriptLength)
                errors.Add(new($"绘图脚本不能超过 {LyricDrawScriptParser.MaximumScriptLength} 个字符。", operation.InstanceId, "script"));

            foreach (var (key, parameter) in operation.Parameters)
            {
                if (parameter.Expression.Length > LyricExpressionCompiler.MaximumExpressionLength)
                    errors.Add(new($"表达式不能超过 {LyricExpressionCompiler.MaximumExpressionLength} 个字符。", operation.InstanceId, key));
            }
        }

        ValidateRequiredOperation(document, LyricBuiltInOperationTypes.Source, "歌词内容", errors);
        ValidateRequiredOperation(document, LyricBuiltInOperationTypes.Debug, "Debug 信息", errors);

        return errors;
    }

    public static LyricEffectProfileDocument MigrateToCurrent(LyricEffectProfileDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SchemaVersion > LyricEffectProfileDocument.CurrentSchemaVersion ||
            source.ExpressionApiVersion > LyricEffectProfileDocument.CurrentExpressionApiVersion)
            throw new NotSupportedException("歌词特效配置来自更高版本的 HyPlayer。");

        var migrated = LyricEffectPresets.CloneProfile(source);
        switch (migrated.SchemaVersion)
        {
            case 0:
                // v0 是开发预览格式；节点字段与 v1 相同，v1 仅固定了根标识和版本语义。
                migrated.Format = LyricEffectProfileDocument.CurrentFormat;
                migrated.SchemaVersion = 2;
                migrated.ExpressionApiVersion = 2;
                migrated.FocusedText = LyricEffectPresets.CreateDefaultFocusedText();
                break;
            case 1:
                migrated.SchemaVersion = 2;
                migrated.ExpressionApiVersion = 2;
                migrated.FocusedText = LyricEffectPresets.CreateDefaultFocusedText();
                break;
            case 2:
                break;
            default:
                throw new NotSupportedException($"无法迁移 schemaVersion {migrated.SchemaVersion}。");
        }

        EnsureRequiredOperation(migrated, LyricBuiltInOperationTypes.Source, LyricEffectPresets.CreateSource, insertAtStart: true);
        EnsureRequiredOperation(migrated, LyricBuiltInOperationTypes.Debug, LyricEffectPresets.CreateDebug, insertAtStart: false);
        migrated.FocusedText ??= LyricEffectPresets.CreateDefaultFocusedText();
        return migrated;
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

    private static void EnsureRequiredOperation(
        LyricEffectProfileDocument document,
        string typeId,
        Func<LyricRenderOperationDefinition> create,
        bool insertAtStart)
    {
        var existing = document.Operations.FirstOrDefault(item => item.TypeId == typeId);
        if (existing is not null)
        {
            existing.IsEnabled = true;
            return;
        }

        if (insertAtStart)
            document.Operations.Insert(0, create());
        else
            document.Operations.Add(create());
    }
}
