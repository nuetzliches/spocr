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
                AutoUpdate = new GlobalAutoUpdateConfigurationModel
                {
                    Enabled = true,
                    PauseInMinutes = 1440,
                    NextCheckTicks = 0
                }
            };
        }

        public ConfigurationModel GetDefaultConfiguration(string appNamespace = "", string connectionString = "", ERoleKind roleKind = default, string libNamespace = null, EIdentityKind identityKind = default)
        {
            var role = new RoleModel
            {
                Kind = roleKind,
                LibNamespace = roleKind == ERoleKind.Extension
                    ? libNamespace
                    : null
            };

            var identity = new IdentityModel
            {
                Kind = identityKind
            };

            return new ConfigurationModel
            {
                Version = Version,
                Modified = DateTime.Now,
                Project = new ProjectModel
                {
                    Role = role,
                    Identity = identity,
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
                            Models = new DataContextModelsModel
                            {
                                Path = "./Models",
                            },
                            Params = new DataContextParamsModel
                            {
                                Path = "./Params",
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