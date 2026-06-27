using HyPlayer.Classes;
using LiteFM;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HyPlayer.Infrastructure.Serialization;

/// <summary>
/// 全局 JSON 序列化选项
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new NumberToStringConverter(), new JsonBooleanConverter(), new JsonObjectStringConverter() },
        WriteIndented = true,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonDefaultContext.Default, LastFMJsonDefaultContext.Default)
    };
}
