using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SpocR.Extensions
{
    internal static class ConfigurationExtensions
    {
        internal static string FileName = "appsettings.json";

        internal static bool FileExists(this IConfiguration configuration)
        {
            return File.Exists(FileName);
        }

        internal static void Save(this IConfiguration configuration)
        {
            var jsonSettings = new JsonSerializerOptions()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true,
                Converters = {
                    new JsonStringEnumConverter()
                }
            };

            var json = JsonSerializer.Serialize(configuration, jsonSettings);
            File.WriteAllText(FileName, json);
        }
    }
}