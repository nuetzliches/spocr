using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Centralizes ScriptDom parsing configuration for procedure analyzers so we can share the same <see cref="TSqlFragment"/> across passes.
/// </summary>
internal static class ProcedureModelScriptDomParser
{
    public static TSqlFragment? Parse(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(definition);
        var fragment = parser.Parse(reader, out _);
        return fragment;
    }
}
