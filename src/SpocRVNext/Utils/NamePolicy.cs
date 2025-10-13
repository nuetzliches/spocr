using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SpocR.SpocRVNext.Utils;

public static class NamePolicy
{
    private static readonly HashSet<string> CSharpKeywords = new(new[]
    {
        "class","namespace","string","int","long","short","byte","bool","decimal","double","float","object","record","struct","event","base","this","new","public","internal","protected","private","static","void","using","return"
    });

    public static string Input(string operation) => Sanitize(operation) + "Input";
    public static string Output(string operation) => Sanitize(operation) + "Output";
    public static string Result(string operation) => Sanitize(operation) + "Result";
    public static string Procedure(string operation) => Sanitize(operation) + "Procedure";
    public static string Row(string operation, string setName) => Sanitize(operation) + Sanitize(setName) + "Row";

    public static (string Schema, string Operation) SplitSchema(string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName)) return ("dbo", "Procedure");
        var parts = operationName.Split('.', 2);
        if (parts.Length == 2)
        {
            return (Sanitize(parts[0]), Sanitize(parts[1]));
        }
        return ("dbo", Sanitize(operationName));
    }

    public static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Name";
        // Remove invalid chars
        var cleaned = Regex.Replace(raw, "[^A-Za-z0-9_]", "");
        if (cleaned.Length == 0) cleaned = "Name";
        if (char.IsDigit(cleaned[0])) cleaned = "N" + cleaned;
        cleaned = char.ToUpperInvariant(cleaned[0]) + (cleaned.Length > 1 ? cleaned.Substring(1) : string.Empty);
        if (CSharpKeywords.Contains(cleaned)) cleaned = "@" + cleaned;
        return cleaned;
    }
}
