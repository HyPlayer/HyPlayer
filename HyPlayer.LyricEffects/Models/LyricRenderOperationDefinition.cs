using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.LyricEffects.Models;

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
