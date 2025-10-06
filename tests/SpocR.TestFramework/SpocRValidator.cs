namespace SpocR.TestFramework;

/// <summary>
/// Validator for SpocR generated code and configurations
/// </summary>
public static class SpocRValidator
{
    /// <summary>
    /// Validates that a SpocR project configuration is correct
    /// </summary>
    public static bool ValidateProjectConfiguration(string configPath, out string[] errors)
    {
        var errorList = new List<string>();

        if (!File.Exists(configPath))
        {
            errorList.Add($"Configuration file not found: {configPath}");
            errors = errorList.ToArray();
            return false;
        }

        // TODO: Add JSON schema validation for spocr.json

        errors = errorList.ToArray();
        return errorList.Count == 0;
    }

    /// <summary>
    /// Validates generated C# code syntax
    /// </summary>
    public static bool ValidateGeneratedCodeSyntax(string code, out string[] errors)
    {
        var errorList = new List<string>();

        if (string.IsNullOrWhiteSpace(code))
        {
            errorList.Add("Generated code is empty or null");
        }

        // TODO: Add Roslyn-based syntax validation

        errors = errorList.ToArray();
        return errorList.Count == 0;
    }
}