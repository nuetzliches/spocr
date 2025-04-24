using System;
using SpocR.Enums;
using SpocR.Models;

namespace SpocR.Services;

public class SpocrService
{
    public readonly Version Version;

    public SpocrService()
    {
        Version = GetType().Assembly.GetName().Version;
    }

    public GlobalConfigurationModel GetGlobalDefaultConfiguration()
    {
        return new GlobalConfigurationModel
        {
            Version = Version,
            TargetFramework = Constants.DefaultTargetFramework.ToFrameworkString(),
            AutoUpdate = new GlobalAutoUpdateConfigurationModel
            {
                Enabled = true,
                LongPauseInMinutes = 1440,
                ShortPauseInMinutes = 15,
                NextCheckTicks = 0
            }
        };
    }

    public ConfigurationModel GetDefaultConfiguration(string targetFramework = null, string appNamespace = "", string connectionString = "", RoleKindEnum roleKind = default, string libNamespace = null)
    {
        var role = new RoleModel
        {
            Kind = roleKind,
            LibNamespace = roleKind == RoleKindEnum.Extension
                ? libNamespace
                : null
        };

        return new ConfigurationModel
        {
            Version = Version,
            TargetFramework = targetFramework ?? Constants.DefaultTargetFramework.ToFrameworkString(),
            Project = new ProjectModel
            {
                Role = role,
                DataBase = new DataBaseModel
                {
                    // the default appsettings.json ConnectString Identifier
                    // you can customize this one later on in the spocr.json
                    RuntimeConnectionStringIdentifier = "DefaultConnection",
                    ConnectionString = connectionString ?? ""
                },
                Output = new OutputModel
                {
                    Namespace = appNamespace,
                    DataContext = new DataContextModel
                    {
                        Path = "./DataContext",
                        Inputs = new DataContextInputsModel
                        {
                            Path = "./Inputs",
                        },
                        Outputs = new DataContextOutputsModel
                        {
                            Path = "./Outputs",
                        },
                        Models = new DataContextModelsModel
                        {
                            Path = "./Models",
                        },
                        TableTypes = new DataContextTableTypesModel
                        {
                            Path = "./TableTypes",
                        },
                        StoredProcedures = new DataContextStoredProceduresModel
                        {
                            Path = "./StoredProcedures",
                        }
                    }
                }
            },
            Schema = []
        };
    }
}