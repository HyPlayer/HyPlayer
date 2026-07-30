using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.LyricEffects.Models;

public sealed class LyricEffectProfileDocument
{
    public const string CurrentFormat = "hyplayer.lyric-effects";
    public const int CurrentSchemaVersion = 2;
    public const int CurrentExpressionApiVersion = 2;

    public string Format { get; set; } = CurrentFormat;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int ExpressionApiVersion { get; set; } = CurrentExpressionApiVersion;

    public string Name { get; set; } = "HyPlayer 默认";

    public List<LyricRenderOperationDefinition> Operations { get; set; } = [];

    public FocusedTextEffectDefinition FocusedText { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<UntimedLyricLineMode>))]
public enum UntimedLyricLineMode
{
    DirectHighlight,
    InferWords
}

[JsonConverter(typeof(JsonStringEnumConverter<HighlightRevealMode>))]
public enum HighlightRevealMode
{
    RectangleClip,
    GlyphStep,
    WholeWord
}

[JsonConverter(typeof(JsonStringEnumConverter<TransliterationProgressMode>))]
public enum TransliterationProgressMode
{
    FollowMain,
    WholeLine
}

public sealed class FocusedTextEffectDefinition
{
    public UntimedLyricLineMode UntimedLineMode { get; set; } = UntimedLyricLineMode.DirectHighlight;

    public HighlightRevealMode HighlightRevealMode { get; set; } = HighlightRevealMode.RectangleClip;

    public TransliterationProgressMode TransliterationMode { get; set; } = TransliterationProgressMode.FollowMain;

    public List<FocusedTextOperationDefinition> Operations { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class FocusedTextOperationDefinition
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");

    public string TypeId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public List<string> Targets { get; set; } = [];

    public Dictionary<string, LyricOperationParameterDefinition> Parameters { get; set; } = [];

    public Dictionary<string, string> Options { get; set; } = [];

    public string? Script { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LyricRenderOperationDefinition
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");

    public string TypeId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public Dictionary<string, LyricOperationParameterDefinition> Parameters { get; set; } = [];

    public Dictionary<string, string> Options { get; set; } = [];

    public string? Script { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LyricOperationParameterDefinition
{
    public string Expression { get; set; } = string.Empty;

    public LyricTransitionDefinition? Transition { get; set; }
}

public sealed class LyricTransitionDefinition
{
    public double DurationMs { get; set; } = 500;

    public string EasingId { get; set; } = "circle";

    public string Mode { get; set; } = "out";

    public Dictionary<string, double> Arguments { get; set; } = [];
}

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
