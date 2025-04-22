using System;

namespace SpocR.Extensions
{
    internal static class VersionExtensions
    {
        internal static string ToVersionString(this Version version)
        {
            return version.ToString(3);
            // return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        internal static int Compare(this Version version, Version compareWith)
        {
            var version1 = Version.Parse(version.ToString(3));
            var version2 = Version.Parse(compareWith.ToString(3));
            return version1.CompareTo(version2);
            // return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        ///
        ///<summary>Checks if the given version is greater (is newer version)</summary>
        ///
        internal static bool IsGreaterThan(this Version version, Version versionToCompare)
        {
            return Compare(version, versionToCompare) > 0;
        }


        ///
        ///<summary>Checks if the given version is lower (is previous version)</summary>
        ///
        internal static bool IsLessThan(this Version version, Version versionToCompare)
        {
            return Compare(version, versionToCompare) < 0;
        }

        ///
        ///<summary>Checks if the given version is the same</summary>
        ///
        internal static bool Equals(this Version version, Version versionToCompare)
        {
            return Compare(version, versionToCompare) == 0;
        }
    }
}