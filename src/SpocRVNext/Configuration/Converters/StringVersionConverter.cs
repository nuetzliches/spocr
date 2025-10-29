using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpocRVNext.Configuration;

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

    public override void Write(Utf8JsonWriter writer, Version version, JsonSerializerOptions options)
    {
        if (version == null)
        {
            writer.WriteNull("Version");
            return;
        }

        writer.WriteStringValue($"{version.Major}.{version.Minor}.{version.Build}");
    }
}
