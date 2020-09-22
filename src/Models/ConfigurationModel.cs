using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SpocR.Attributes;
using SpocR.Converters;
using SpocR.Enums;
using SpocR.Interfaces;

namespace SpocR.Models
{
    public class GlobalConfigurationModel : IVersioned
    {
        [JsonConverter(typeof(StringVersionConverter)), WriteProtectedBySystem]
        public Version Version { get; set; }
        public string UserId { get; set; }
        public GlobalAutoUpdateConfigurationModel AutoUpdate { get; set; } = new GlobalAutoUpdateConfigurationModel { Enabled = true, PauseInMinutes = 1440 };
        public List<GlobalProjectConfigurationModel> Projects { get; set; } = new List<GlobalProjectConfigurationModel>();
    }

    public class GlobalProjectConfigurationModel
    {
        public string DisplayName { get; set; }
        public string ConfigFile { get; set; }

    }

    public class GlobalAutoUpdateConfigurationModel
    {
        public bool Enabled { get; set; }
        public int PauseInMinutes { get; set; }
        public long NextCheckTicks { get; set; }
    }

    public class ConfigurationModel : IVersioned
    {
        [JsonConverter(typeof(StringVersionConverter))]
        public Version Version { get; set; }
        public DateTime Modified { get; set; }
        public ProjectModel Project { get; set; }
        public List<SchemaModel> Schema { get; set; }
    }

    public class ProjectModel
    {
        public RoleModel Role { get; set; } = new RoleModel();
        public IdentityModel Identity { get; set; } = new IdentityModel();
        public DataBaseModel DataBase { get; set; } = new DataBaseModel();
        public OutputModel Output { get; set; } = new OutputModel();
    }

    public class RoleModel
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ERoleKind Kind { get; set; } = ERoleKind.Default;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string LibNamespace { get; set; }
    }

    public class IdentityModel
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public EIdentityKind Kind { get; set; } = EIdentityKind.WithUserId;
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
        public DataContextInputsModel Inputs { get; set; }
        public DataContextModelsModel Models { get; set; }
        public DataContextParamsModel Params { get; set; }
        public DataContextStoredProceduresModel StoredProcedures { get; set; }
    }

    public class DataContextInputsModel : IDirectoryModel
    {
        public string Path { get; set; }
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
}