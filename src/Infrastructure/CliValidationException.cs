using System;

namespace SpocR.Infrastructure;

/// <summary>
/// Exception type used to signal expected CLI validation problems (e.g., missing configuration, invalid combinations).
/// Handled explicitly by <see cref="Program"/> to produce friendly error messages and validation exit codes.
/// </summary>
public class CliValidationException : Exception
{
    public CliValidationException(string message)
        : base(message)
    {
    }

    public CliValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
