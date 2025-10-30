using Shouldly;
using Xunit;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Infrastructure;

namespace SpocR.Tests.Cli;

public class RoleDeprecationTests
{
    [Fact]
    public async Task Save_DoesNotEmit_Role_Section()
    {
        // Arrange
        var cfg = new ConfigurationModel
        {
            Project = new ProjectModel
            {
                Output = new OutputModel { Namespace = "Ns", DataContext = new DataContextModel { Path = "./dc" } },
                DataBase = new DataBaseModel { ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;" }
            }
        };
        var service = new SpocR.SpocRVNext.Services.SpocrService();
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json");
        var fm = new FileManager<ConfigurationModel>(service, tempFile);
        await fm.SaveAsync(cfg);
        System.IO.File.Exists(tempFile).ShouldBeTrue();
        var json = System.IO.File.ReadAllText(tempFile);
        json.ShouldNotContain("\"Role\":");
    }

    [Fact]
        public async Task Load_Ignores_Legacy_Role()
    {
        var service = new SpocR.SpocRVNext.Services.SpocrService();
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json");
                        var legacyJson = """
                        {
                            "Project": {
                                "Role": {
                                    "Kind": "lib",
                                    "LibNamespace": "Legacy.Namespace"
                                },
                                "Output": {
                                    "Namespace": "Ns",
                                    "DataContext": {
                                        "Path": "./dc"
                                    }
                                },
                                "DataBase": {
                                    "ConnectionString": "cs"
                                }
                            }
                        }
                        """;
                System.IO.File.WriteAllText(tempFile, legacyJson);

                var fm = new FileManager<ConfigurationModel>(service, tempFile);
                var loaded = await fm.ReadAsync();

                loaded.ShouldNotBeNull();
                loaded!.Project.ShouldNotBeNull();
                loaded.Project.Output.Namespace.ShouldBe("Ns");
                loaded.Project.DataBase.ConnectionString.ShouldBe("cs");
    }
}
