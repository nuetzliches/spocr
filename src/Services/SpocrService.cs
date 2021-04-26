using System;
using System.Collections.Generic;
using SpocR.Enums;
using SpocR.Models;

namespace SpocR.Services
{
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
                TargetFramework = "net5.0",
                AutoUpdate = new GlobalAutoUpdateConfigurationModel
                {
                    Enabled = true,
                    LongPauseInMinutes = 1440,
                    ShortPauseInMinutes = 15,
                    NextCheckTicks = 0
                }
            };
        }

        public ConfigurationModel GetDefaultConfiguration(string targetFramework = "net5.0", string appNamespace = "", string connectionString = "", ERoleKind roleKind = default, string libNamespace = null /*, EIdentityKind identityKind = default */)
        {
            var role = new RoleModel
            {
                Kind = roleKind,
                LibNamespace = roleKind == ERoleKind.Extension
                    ? libNamespace
                    : null
            };

            // var identity = new IdentityModel
            // {
            //     Kind = identityKind
            // };

            return new ConfigurationModel
            {
                Version = Version,
                TargetFramework = targetFramework,
                Project = new ProjectModel
                {
                    Role = role,
                    // Identity = identity,
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
                Schema = new List<SchemaModel>()
            };
        }
    }
}