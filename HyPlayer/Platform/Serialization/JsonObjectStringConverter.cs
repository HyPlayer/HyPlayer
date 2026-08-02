using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.Platform.Serialization;

public sealed class JsonObjectStringConverter : JsonConverter<JsonObjectStringWrapper>
{
    public override JsonObjectStringWrapper? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new JsonObjectStringWrapper(reader.GetString()),
            JsonTokenType.StartObject => new JsonObjectStringWrapper(JsonDocument.ParseValue(ref reader).RootElement
                .ToString()),
            JsonTokenType.Number => new JsonObjectStringWrapper(reader.GetInt64().ToString()),
            JsonTokenType.Null => new JsonObjectStringWrapper(null),
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    public override JsonObjectStringWrapper ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return Read(ref reader, typeToConvert, options) ?? new JsonObjectStringWrapper(null);
    }

    public override void Write(Utf8JsonWriter writer, JsonObjectStringWrapper value, JsonSerializerOptions options)
    {
        if (value.Value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, JsonObjectStringWrapper value,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.Value ?? string.Empty);
    }
}

public sealed class JsonObjectStringWrapper(string? value)
{
    public string? Value { get; set; } = value;
}
