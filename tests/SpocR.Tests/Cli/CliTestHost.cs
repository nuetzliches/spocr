using System.Threading.Tasks;

namespace SpocR.Tests.Cli;

/// <summary>
/// Thin wrapper used by tests to invoke the SpocR CLI in-process.
/// Exists because some build contexts showed the Program.RunCliAsync symbol
/// as unavailable to the test project despite being public (likely due to multi-target nuances).
/// </summary>
public static class CliTestHost
{
    public static Task<int> RunAsync(params string[] args) => SpocR.Program.RunCliAsync(args);
}
