using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HyPlayer.Classes;
using LiteFM;

namespace HyPlayer.Platform.Serialization;

/// <summary>
///     全局 JSON 序列化选项
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new NumberToStringConverter(), new JsonBooleanConverter(), new JsonObjectStringConverter() },
        WriteIndented = true,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonDefaultContext.Default, LastFMJsonDefaultContext.Default)
    };

    static JsonDefaults()
    {
        // Read-only options cache resolved metadata; repeated persistence should not recreate it.
        Options.MakeReadOnly();
    }

    /// <summary>Resolve source-generated metadata using the application's converters and naming policy.</summary>
    public static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());
}
