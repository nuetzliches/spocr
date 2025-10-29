using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Enums;

namespace SpocRVNext.Configuration;

public class TargetFrameworkConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Constants.DefaultTargetFramework.ToFrameworkString();

        string value = reader.GetString();

        TargetFrameworkEnum framework = TargetFrameworkExtensions.FromString(value);

        // Normalize the string (for consistency)
        return framework.ToFrameworkString();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteStringValue(Constants.DefaultTargetFramework.ToFrameworkString());
        }
        else
        {
            // Normalize the string (for consistency)
            TargetFrameworkEnum framework = TargetFrameworkExtensions.FromString(value);
            writer.WriteStringValue(framework.ToFrameworkString());
        }
    }
}
