using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpocR.SpocRVNext.Configuration;

public sealed class StringVersionConverter : JsonConverter<Version?>
{
    public override Version? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var versionText = reader.GetString();
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        try
        {
            return Version.Parse(versionText);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, Version? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
