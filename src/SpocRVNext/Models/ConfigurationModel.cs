using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Configuration;
using SpocR.SpocRVNext.Infrastructure;

namespace SpocR.SpocRVNext.Models;

public class ConfigurationModel : IVersioned
{
    [JsonConverter(typeof(StringVersionConverter))]
    public Version? Version { get; set; }

    [JsonConverter(typeof(TargetFrameworkConverter))]
    public string TargetFramework { get; set; } = Constants.DefaultTargetFramework.ToFrameworkString(); // Erlaubte Werte: netcoreapp2.2, net6.0, net8.0, net9.0

    public ProjectModel Project { get; set; } = new();
    public List<SchemaModel> Schema { get; set; } = new();
}
