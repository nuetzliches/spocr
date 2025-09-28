using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SpocR.Models;

public class StoredProcedureContentModel
{
    private static readonly Regex ForJsonRegex = new(@"FOR\s+JSON\s+(?<mode>PATH|AUTO|ROOT)(?<options>[^;]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JsonRootRegex = new(@"ROOT\s*\(\s*'(?<root>[^']+)'\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WithoutArrayWrapperRegex = new(@"WITHOUT\s+ARRAY\s+WRAPPER", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StatementSplitterRegex = new(@"(?:^|\s)GO(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Definition { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> Statements { get; init; } = Array.Empty<string>();

    public bool ContainsSelect { get; init; }
    public bool ContainsInsert { get; init; }
    public bool ContainsUpdate { get; init; }
    public bool ContainsDelete { get; init; }
    public bool ContainsMerge { get; init; }

    public bool ReturnsJson { get; init; }
    public bool ReturnsJsonArray { get; init; }
    public bool ReturnsJsonWithoutArrayWrapper { get; init; }
    public string JsonRootProperty { get; init; }
    public bool ContainsOpenJson { get; init; }

    public static StoredProcedureContentModel Parse(string definition)
    {
        var model = new StoredProcedureContentModel
        {
            Definition = definition
        };

        if (string.IsNullOrWhiteSpace(definition))
        {
            return model;
        }

        var statements = StatementSplitterRegex.Split(definition)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        var containsSelect = Regex.IsMatch(definition, @"\bSELECT\b", RegexOptions.IgnoreCase);
        var containsInsert = Regex.IsMatch(definition, @"\bINSERT\b", RegexOptions.IgnoreCase);
        var containsUpdate = Regex.IsMatch(definition, @"\bUPDATE\b", RegexOptions.IgnoreCase);
        var containsDelete = Regex.IsMatch(definition, @"\bDELETE\b", RegexOptions.IgnoreCase);
        var containsMerge = Regex.IsMatch(definition, @"\bMERGE\b", RegexOptions.IgnoreCase);
        var containsOpenJson = Regex.IsMatch(definition, @"\bOPENJSON\b", RegexOptions.IgnoreCase);

        var forJsonMatches = ForJsonRegex.Matches(definition);
        var returnsJson = forJsonMatches.Count > 0;
        var anyWithoutArrayWrapper = false;
        var anyWithArrayWrapper = false;
        string jsonRoot = null;

        foreach (Match forJson in forJsonMatches)
        {
            var options = forJson.Groups["options"].Value;
            if (WithoutArrayWrapperRegex.IsMatch(options))
            {
                anyWithoutArrayWrapper = true;
            }
            else
            {
                anyWithArrayWrapper = true;
            }

            if (jsonRoot == null)
            {
                var rootMatch = JsonRootRegex.Match(options);
                if (rootMatch.Success)
                {
                    jsonRoot = rootMatch.Groups["root"].Value;
                }
            }
        }

        return new StoredProcedureContentModel
        {
            Definition = definition,
            Statements = statements,
            ContainsSelect = containsSelect,
            ContainsInsert = containsInsert,
            ContainsUpdate = containsUpdate,
            ContainsDelete = containsDelete,
            ContainsMerge = containsMerge,
            ContainsOpenJson = containsOpenJson,
            ReturnsJson = returnsJson,
            ReturnsJsonWithoutArrayWrapper = anyWithoutArrayWrapper,
            ReturnsJsonArray = returnsJson && (anyWithArrayWrapper || !anyWithoutArrayWrapper),
            JsonRootProperty = jsonRoot
        };
    }
}
