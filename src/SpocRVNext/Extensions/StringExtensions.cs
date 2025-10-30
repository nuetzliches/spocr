using System;
using System.Text;

namespace SpocR.SpocRVNext.Extensions;

internal static class StringExtensions
{
    internal static string FirstCharToLower(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return string.Concat(input[0].ToString().ToLowerInvariant(), input.Substring(1));
    }

    internal static string FirstCharToUpper(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return string.Concat(input[0].ToString().ToUpperInvariant(), input.Substring(1));
    }

    internal static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = new StringBuilder();
        var capitalizeNext = true;

        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                if (capitalizeNext)
                {
                    result.Append(char.ToUpperInvariant(c));
                    capitalizeNext = false;
                }
                else
                {
                    result.Append(c);
                }
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }

        return result.ToString();
    }
}
