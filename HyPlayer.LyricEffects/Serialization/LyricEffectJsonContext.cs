using HyPlayer.LyricEffects.Models;
using System.Text.Json.Serialization;

namespace HyPlayer.LyricEffects.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LyricEffectProfileDocument))]
[JsonSerializable(typeof(LyricRenderOperationDefinition))]
[JsonSerializable(typeof(LyricOperationParameterDefinition))]
[JsonSerializable(typeof(LyricTransitionDefinition))]
[JsonSerializable(typeof(List<LyricRenderOperationDefinition>))]
public partial class LyricEffectJsonContext : JsonSerializerContext
{
}
