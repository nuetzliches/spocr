using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SpocR.Enums;
using SpocR.Internal.Models;

namespace SpocR.Internal.Models
{
    public class ConfigurationModel
    {
        public Version Version { get; set; }
        public DateTime Modified { get; set; }
        public ProjectModel Project { get; set; }
        public IEnumerable<SchemaModel> Schema { get; set; }
    }

    public class ProjectModel
    {
        public RoleModel Role { get; set; }
        public DataBaseModel DataBase { get; set; }
        public IEnumerable<OutputModel> Output { get; set; }
    }

    public class RoleModel
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ERoleKind Kind { get; set; } = ERoleKind.Default;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string LibNamespace { get; set; }
    }

    public class DataBaseModel
    {
        public string RuntimeConnectionStringIdentifier { get; set; }
        public string ConnectionString { get; set; }
    }

    public class OutputModel
    {
        public string Namespace { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public IEnumerable<OutputModel> Children { get; set; }
    }

    public class ConfigurationJsonModel
    {
        private readonly ConfigurationModel _item;
        public ConfigurationJsonModel(ConfigurationModel item)
        {
            _item = item;
        }
        public string Version => $"{_item.Version.Major}.{_item.Version.Minor}.{_item.Version.Build}";
        public DateTime Modified => _item.Modified;
        public ProjectModel Project => _item.Project;
        public IEnumerable<SchemaModel> Schema => _item.Schema;
    }
}