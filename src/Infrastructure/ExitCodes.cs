namespace SpocR.Infrastructure;

/// <summary>
/// Central definition of process exit codes used by the SpocR CLI.
/// Keep in sync with the Exit Codes section in the README.
/// Versioned Exit Code Map (Option B – spaced category blocks)
///  0   : Success
/// 10   : Validation/User Error
/// 20   : Generation Error
/// 30   : Dependency / External Error
/// 40   : Test Failure (aggregate; future: 41 unit, 42 integration, 43 validation)
/// 50   : Benchmark Failure
/// 60   : Rollback / Recovery Failure
/// 70   : Configuration Error
/// 80   : Internal Unexpected Error
/// 99   : Reserved / Future
/// </summary>
public static class ExitCodes
{
    /// <summary>Successful execution.</summary>
    public const int Success = 0;

    /// <summary>Validation or user input error (non-fatal domain level).</summary>
    public const int ValidationError = 10;

    /// <summary>Code generation pipeline error.</summary>
    public const int GenerationError = 20;

    /// <summary>External dependency / environment / network / database failure.</summary>
    public const int DependencyError = 30;

    /// <summary>Test suite failure (aggregate).</summary>
    public const int TestFailure = 40;

    /// <summary>Benchmark execution failure.</summary>
    public const int BenchmarkFailure = 50;

    /// <summary>Rollback / recovery operation failure.</summary>
    public const int RollbackFailure = 60;

    /// <summary>Configuration parsing / validation failure.</summary>
    public const int ConfigurationError = 70;

    /// <summary>Unexpected internal error / unhandled exception.</summary>
    public const int InternalError = 80;

    /// <summary>Reserved for future extension / experimental features.</summary>
    public const int Reserved = 99;
}
