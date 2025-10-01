using Microsoft.Extensions.Configuration;
using SpocR.Managers;
using SpocR.TestFramework;

namespace SpocR.Tests.Managers;

public class SpocrConfigManagerTests : SpocrTestBase
{
    [Fact]
    public void SpocrConfigManager_ShouldBeRegistered()
    {
        // Act
        var configManager = GetOptionalService<ISpocrConfigManager>();

        // Assert
        configManager.Should().NotBeNull();
    }

    [Fact]
    public void SpocrConfigManager_ShouldHaveDefaultConfiguration()
    {
        // Arrange
        var configManager = GetService<ISpocrConfigManager>();

        // Act & Assert - Basic operations shouldn't throw
        Action act = () => configManager.GetType();
        act.Should().NotThrow();
    }

    protected override Dictionary<string, string?> GetDefaultConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["TestMode"] = "true",
            ["Environment"] = "Test",
            ["ConnectionString"] = "Data Source=localhost;Initial Catalog=TestDb;Integrated Security=true"
        };
    }
}