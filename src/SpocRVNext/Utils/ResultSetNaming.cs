using System;
using System.Collections.Generic;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.SpocRVNext.Utils;

public static class ResultSetNaming
{
    public static string DeriveName(int index, IReadOnlyList<FieldDescriptor> fields, ISet<string> existing)
    {
        var prefix = TryCommonPrefix(fields);
        string baseName = string.IsNullOrWhiteSpace(prefix) ? $"ResultSet{index + 1}" : NamePolicy.Sanitize(prefix);
        var candidate = baseName;
        var counter = 2;
        while (existing.Contains(candidate))
        {
            candidate = baseName + counter;
            counter++;
        }
        existing.Add(candidate);
        return candidate;
    }

    private static string? TryCommonPrefix(IReadOnlyList<FieldDescriptor> fields)
    {
        if (fields.Count < 2) return null;
        string prefix = fields[0].PropertyName;
        for (int i = 1; i < fields.Count && prefix.Length > 0; i++)
        {
            prefix = LongestCommonPrefix(prefix, fields[i].PropertyName);
        }
        if (prefix.Length < 3) return null;
        return prefix;
    }

    private static string LongestCommonPrefix(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        for (; i < len; i++)
        {
            if (a[i] != b[i]) break;
        }
        return a.Substring(0, i);
    }
}
