using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.Infrastructure.Serialization;

public sealed partial class NumberToStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException("Unexpected token type within NumberToStringConverter")
        };
    }

    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() ?? string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value);
    }
}

public sealed partial class JsonBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => !(reader.GetString() == "false" || string.IsNullOrWhiteSpace(reader.GetString())),
            JsonTokenType.Number => reader.GetInt32() > 0,
            JsonTokenType.True => true,
            _ => false
        };
    }

    public override bool ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString());
    }
}

public sealed partial class JsonObjectStringConverter : JsonConverter<JsonObjectStringWrapper>
{
    public override JsonObjectStringWrapper? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new JsonObjectStringWrapper(reader.GetString()),
            JsonTokenType.StartObject => new JsonObjectStringWrapper(JsonDocument.ParseValue(ref reader).RootElement.ToString()),
            JsonTokenType.Number => new JsonObjectStringWrapper(reader.GetInt64().ToString()),
            JsonTokenType.Null => new JsonObjectStringWrapper(null),
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    public override JsonObjectStringWrapper ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

    public override void WriteAsPropertyName(Utf8JsonWriter writer, JsonObjectStringWrapper value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.Value ?? string.Empty);
    }
}

public sealed partial class JsonObjectStringWrapper(string? value)
{
    public string? Value { get; set; } = value;
}
