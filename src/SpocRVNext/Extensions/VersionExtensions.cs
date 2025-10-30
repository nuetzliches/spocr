using System;

namespace SpocR.SpocRVNext.Extensions;

internal static class VersionExtensions
{
    internal static string ToVersionString(this Version version)
    {
        return version.ToString(3);
    }

    internal static int Compare(this Version version, Version compareWith)
    {
        var version1 = Version.Parse(version.ToString(3));
        var version2 = Version.Parse(compareWith.ToString(3));
        return version1.CompareTo(version2);
    }

    internal static bool IsGreaterThan(this Version version, Version versionToCompare)
    {
        return Compare(version, versionToCompare) > 0;
    }

    internal static bool IsLessThan(this Version version, Version versionToCompare)
    {
        return Compare(version, versionToCompare) < 0;
    }

    internal static bool Equals(this Version version, Version versionToCompare)
    {
        return Compare(version, versionToCompare) == 0;
    }
}
