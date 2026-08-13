namespace HyPlayer.LyricEffects.Models;

public enum LyricExpressionValueType
{
    Scalar,
    Color,
    Text
}

public sealed class LyricOperationParameterDescriptor
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public required LyricExpressionValueType ValueType { get; init; }

    public required string DefaultExpression { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool SupportsTransition { get; init; }

    public float? Minimum { get; init; }

    public float? Maximum { get; init; }
}

public enum LyricRenderOperationCategory
{
    Effect,
    Draw
}

public sealed class LyricRenderOperationDescriptor
{
    public required string TypeId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<LyricOperationParameterDescriptor> Parameters { get; init; }

    public LyricRenderOperationCategory Category { get; init; } = LyricRenderOperationCategory.Effect;

    public bool IsEditable { get; init; } = true;

    public bool IsRequired { get; init; }

    public bool SupportsScript { get; init; }
}
