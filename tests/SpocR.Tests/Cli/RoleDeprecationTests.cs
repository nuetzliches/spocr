using System.Text.Json;
using Shouldly;
using Xunit;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Infrastructure;
using SpocRVNext.Configuration;

namespace SpocR.Tests.Cli;

public class RoleDeprecationTests
{
    [Fact]
    public async Task Save_Removes_Role_When_Default()
    {
        // Arrange
        var cfg = new ConfigurationModel
        {
            Project = new ProjectModel
            {
                Role = new RoleModel { Kind = RoleKindEnum.Default },
                Output = new OutputModel { Namespace = "Ns", DataContext = new DataContextModel { Path = "./dc" } },
                DataBase = new DataBaseModel { ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;" }
            }
        };
        var service = new SpocR.Services.SpocrService();
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json");
        var fm = new FileManager<ConfigurationModel>(service, tempFile);
        await fm.SaveAsync(cfg);
        System.IO.File.Exists(tempFile).ShouldBeTrue();
        var json = System.IO.File.ReadAllText(tempFile);
        json.ShouldNotContain("\"Role\":");
    }

    [Fact]
    public async Task Keep_Role_When_NonDefault()
    {
        var cfg = new ConfigurationModel
        {
            Project = new ProjectModel
            {
                Role = new RoleModel { Kind = RoleKindEnum.Lib },
                Output = new OutputModel { Namespace = "Ns", DataContext = new DataContextModel { Path = "./dc" } },
                DataBase = new DataBaseModel { ConnectionString = "cs" }
            }
        };
        var service = new SpocR.Services.SpocrService();
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json");
        var fm = new FileManager<ConfigurationModel>(service, tempFile);
        await fm.SaveAsync(cfg);
        System.IO.File.Exists(tempFile).ShouldBeTrue();
        var json = System.IO.File.ReadAllText(tempFile);
        json.ShouldContain("\"Role\":");
    }
}
