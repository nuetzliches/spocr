using System;

namespace SpocR.Extensions
{
    internal static class StringExtensions
    {
        internal static string FirstCharToLower(this string input)
        {
            return $"{input[0].ToString().ToLowerInvariant()}{input.Remove(0, 1)}";
        }
    }
}