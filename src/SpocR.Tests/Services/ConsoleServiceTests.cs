using Microsoft.Extensions.DependencyInjection;
using SpocR.Services;
using SpocR.TestFramework;

namespace SpocR.Tests.Services;

public class ConsoleServiceTests : SpocrTestBase
{
    [Fact]
    public void ConsoleService_ShouldBeRegistered()
    {
        // Act
        var consoleService = GetOptionalService<IConsoleService>();

        // Assert
        consoleService.Should().NotBeNull();
        consoleService.Should().BeOfType<ConsoleService>();
    }

    [Fact]
    public void ConsoleService_ShouldSupportBasicLogging()
    {
        // Arrange
        var consoleService = GetService<IConsoleService>();

        // Act & Assert - These shouldn't throw
        consoleService.Info("Test info message");
        consoleService.Error("Test error message");
        consoleService.Warn("Test warning message");
        consoleService.Verbose("Test verbose message");
    }

    [Theory]
    [InlineData("Simple message")]
    [InlineData("Message with special characters: äöü !@#$%")]
    [InlineData("Multi\nLine\nMessage")]
    [InlineData("")]
    public void ConsoleService_ShouldHandleVariousMessageFormats(string message)
    {
        // Arrange
        var consoleService = GetService<IConsoleService>();

        // Act & Assert - These shouldn't throw
        Action act = () => consoleService.Info(message);
        act.Should().NotThrow();
    }
}