using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.LyricEffects.Models;

public sealed class LyricEffectProfileDocument
{
    public const string CurrentFormat = "hyplayer.lyric-effects";
    public const int CurrentSchemaVersion = 1;
    public const int CurrentExpressionApiVersion = 1;

    public string Format { get; set; } = CurrentFormat;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int ExpressionApiVersion { get; set; } = CurrentExpressionApiVersion;

    public string Name { get; set; } = "HyPlayer 默认";

    public List<LyricRenderOperationDefinition> Operations { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
