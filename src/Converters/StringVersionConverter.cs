using System;
using Newtonsoft.Json;

namespace SpocR.Converters {

    public class StringVersionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var versionText = reader.Value?.ToString();
            if (string.IsNullOrWhiteSpace(versionText))
            {
                return null;
            }
            return new Version(versionText);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var version = (Version)value;
            writer.WriteValue($"{version.Major}.{version.Minor}.{version.Build}");
        }
    }

}
