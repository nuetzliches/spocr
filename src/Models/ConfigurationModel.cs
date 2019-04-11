using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SpocR.Enums;
using SpocR.Models;

namespace SpocR.Models
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
        public OutputModel Output { get; set; }
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
        public DataContextModel DataContext { get; set; }
    }

    public class DataContextModel : IDirectoryModel
    {
        public string Path { get; set; }
        public DataContextModelsModel Models { get; set; }
        public DataContextParamsModel Params { get; set; }
        public DataContextStoredProceduresModel StoredProcedures { get; set; }
    }

    public class DataContextModelsModel : IDirectoryModel
    {
        public string Path { get; set; }
    }
    public class DataContextParamsModel : IDirectoryModel
    {
        public string Path { get; set; }
    }
    public class DataContextStoredProceduresModel : IDirectoryModel
    {
        public string Path { get; set; }
    }

    public interface IDirectoryModel
    {
        string Path { get; set; }
    }


    // "DataContext": {
    //     "Path": "./DataContext",
    //     "Models": {
    //       "Path": "./Models"
    //     },
    //     "Params": {
    //       "Path": "./Params"
    //     },
    //     "StoredProcedures": {
    //       "Path": "./StoredProcedures"
    //     }
    //   }

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