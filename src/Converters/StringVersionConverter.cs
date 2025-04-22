using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpocR.Converters;


public class StringVersionConverter : JsonConverter<Version>
{
    public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var versionText = reader.GetString();
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }
        return new Version(versionText);
    }

    public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNull("Version");
            return;
        }

        var version = (Version)value;
        writer.WriteStringValue($"{version.Major}.{version.Minor}.{version.Build}");
    }
}
