using System.IO;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SpocR.Serialization;

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
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new SerializeContractResolver()
            };
            var json = JsonConvert.SerializeObject(configuration, Formatting.Indented, jsonSettings);
            File.WriteAllText(FileName, json);
        }
    }
}