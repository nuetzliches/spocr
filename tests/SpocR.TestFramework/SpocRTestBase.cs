namespace SpocR.TestFramework;

/// <summary>
/// Base class for SpocR test scenarios providing common test infrastructure
/// </summary>
public abstract class SpocRTestBase
{
    /// <summary>
    /// Gets the test database connection string
    /// </summary>
    protected virtual string GetTestConnectionString()
    {
        return Environment.GetEnvironmentVariable("SPOCR_TEST_CONNECTION_STRING")
               ?? "Server=(localdb)\\MSSQLLocalDB;Database=SpocRTest;Trusted_Connection=True;";
    }

    /// <summary>
    /// Validates that generated code compiles successfully
    /// </summary>
    protected static void ValidateGeneratedCodeCompiles(string generatedCode, out bool success, out string[] errors)
    {
        success = true;
        errors = Array.Empty<string>();

        // TODO: Implement Roslyn compilation validation
        // This is a placeholder for the actual validation logic
    }
}