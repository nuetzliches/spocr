using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Enums;

namespace SpocR.Converters;

public class TargetFrameworkConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Constants.DefaultTargetFramework.ToFrameworkString();

        string value = reader.GetString();

        TargetFrameworkEnum framework = TargetFrameworkExtensions.FromString(value);

        // Normalisieren des Strings (für Konsistenz)
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
            // Normalisieren des Strings (für Konsistenz)
            TargetFrameworkEnum framework = TargetFrameworkExtensions.FromString(value);
            writer.WriteStringValue(framework.ToFrameworkString());
        }
    }
}