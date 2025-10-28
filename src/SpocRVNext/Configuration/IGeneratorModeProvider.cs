namespace SpocRVNext.Configuration;

/// <summary>
/// Provides the current generator mode to vNext generators while remaining injectable for tests.
/// </summary>
public interface IGeneratorModeProvider
{
    string Mode { get; }
}

/// <summary>
/// Default implementation representing the enforced next-only mode.
/// </summary>
public sealed class EnvGeneratorModeProvider : IGeneratorModeProvider
{
    public string Mode => "next";
}