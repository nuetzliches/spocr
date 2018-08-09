using System;

namespace SpocR.Internal.Extensions
{
    internal static class VersionExtensions
    {
        internal static string ToVersionString(this Version version)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}