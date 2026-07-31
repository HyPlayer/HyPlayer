using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyPlayer.Platform.Serialization;

public sealed class JsonBooleanConverter : JsonConverter<bool>
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

    public override bool ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
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
