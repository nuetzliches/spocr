namespace SpocR.SpocRVNext.Infrastructure;

/// <summary>
/// Central definition of process exit codes used by the SpocR CLI.
/// Keep in sync with the Exit Codes section in the README.
/// Versioned Exit Code Map (Option B – spaced category blocks)
///  0   : Success
/// 10   : Validation/User Error
/// 20   : Generation Error
/// 21   : Generation Non-Deterministic (hash drift)
/// 22   : Generation Missing Artifact
/// 23   : Generation Diff Anomaly (unexpected structural diff)
/// 30   : Dependency / External Error
/// 40   : Test Failure (aggregate; future: 41 unit, 42 integration, 43 validation)
/// 41   : Unit Test Failure
/// 42   : Integration Test Failure
/// 43   : Validation Test Failure (generated project/repo validation phase)
/// 50   : Benchmark Failure
/// 60   : Rollback / Recovery Failure
/// 70   : Configuration Error
/// 80   : Internal Unexpected Error
/// 99   : Reserved / Future
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ValidationError = 10;
    public const int GenerationError = 20;
    public const int GenerationNonDeterministic = 21;
    public const int GenerationMissingArtifact = 22;
    public const int GenerationDiffAnomaly = 23;
    public const int DependencyError = 30;
    public const int TestFailure = 40;
    public const int UnitTestFailure = 41;
    public const int IntegrationTestFailure = 42;
    public const int ValidationTestFailure = 43;
    public const int BenchmarkFailure = 50;
    public const int RollbackFailure = 60;
    public const int ConfigurationError = 70;
    public const int InternalError = 80;
    public const int Reserved = 99;
}
