using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SpocRVNext.Configuration;
using SpocR.Enums;
using SpocR.Interfaces;

namespace SpocR.Models;

public class GlobalConfigurationModel : IVersioned
{
    [JsonConverter(typeof(StringVersionConverter))]
    public Version Version { get; set; }

    [JsonConverter(typeof(TargetFrameworkConverter))]
    public string TargetFramework { get; set; } = Constants.DefaultTargetFramework.ToFrameworkString();

    public string UserId { get; set; }
    public GlobalAutoUpdateConfigurationModel AutoUpdate { get; set; } = new GlobalAutoUpdateConfigurationModel { Enabled = true, LongPauseInMinutes = 1440, ShortPauseInMinutes = 15 };
    public List<GlobalProjectConfigurationModel> Projects { get; set; } = [];
}

public class GlobalProjectConfigurationModel
{
    public string DisplayName { get; set; }
    public string ConfigFile { get; set; }
}

public class GlobalAutoUpdateConfigurationModel
{
    public bool Enabled { get; set; }
    public string SkipVersion { get; set; }
    public int ShortPauseInMinutes { get; set; }
    public int LongPauseInMinutes { get; set; }
    public long NextCheckTicks { get; set; }
}

public class ConfigurationModel : IVersioned
{
    [JsonConverter(typeof(StringVersionConverter))]
    public Version Version { get; set; }

    [JsonConverter(typeof(TargetFrameworkConverter))]
    public string TargetFramework { get; set; } = Constants.DefaultTargetFramework.ToFrameworkString(); // Erlaubte Werte: netcoreapp2.2, net6.0, net8.0, net9.0

    public ProjectModel Project { get; set; }
    public List<SchemaModel> Schema { get; set; }
}

public class ProjectModel
{
    public RoleModel Role { get; set; } = new RoleModel();
    public DataBaseModel DataBase { get; set; } = new DataBaseModel();
    public OutputModel Output { get; set; } = new OutputModel();
    public SchemaStatusEnum DefaultSchemaStatus { get; set; } = SchemaStatusEnum.Build;
    // New: list of schema names that are explicitly ignored (migration target replacing legacy Schema list)
    public List<string> IgnoredSchemas { get; set; } = new();
    // New: list of fully-qualified procedure names (schema.name) to ignore even if schema is built
    public List<string> IgnoredProcedures { get; set; } = new();
    // Controls verbosity of JSON/procedure typing logs: Detailed (default), SummaryOnly, Off
    public JsonTypeLogLevel JsonTypeLogLevel { get; set; } = JsonTypeLogLevel.Detailed;
}

public enum JsonTypeLogLevel
{
    Detailed = 0,
    SummaryOnly = 1,
    Off = 2
}

public class RoleModel
{
    public RoleKindEnum Kind { get; set; } = RoleKindEnum.Default;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    public DataContextInputsModel Inputs { get; set; }
    public DataContextOutputsModel Outputs { get; set; }
    public DataContextModelsModel Models { get; set; }
    public DataContextStoredProceduresModel StoredProcedures { get; set; }
    public DataContextTableTypesModel TableTypes { get; set; }
}

public class DataContextInputsModel : IDirectoryModel
{
    public string Path { get; set; }
}

public class DataContextOutputsModel : IDirectoryModel
{
    public string Path { get; set; }
}

public class DataContextModelsModel : IDirectoryModel
{
    public string Path { get; set; }
}

public class DataContextTableTypesModel : IDirectoryModel
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
