using System;
using SpocR.SpocRVNext.Configuration;
using SpocR.SpocRVNext.Models;

namespace SpocR.SpocRVNext.Services;

public class SpocrService
{
    public readonly Version Version;

    public SpocrService()
    {
        Version = GetType().Assembly.GetName().Version ?? new Version(0, 0);
    }

    public ConfigurationModel GetDefaultConfiguration(string? targetFramework = null, string appNamespace = "", string connectionString = "", RoleKindEnum roleKind = default, string? libNamespace = null)
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