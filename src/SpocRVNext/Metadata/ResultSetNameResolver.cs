using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Heuristischer Resolver für ResultSet-Namen aus dem ursprünglichen Stored Procedure T-SQL.
/// Non-blocking: Only provides a suggestion; fallback handled by existing ResultSet naming.
/// Strategie:
/// 1. Falls JSON PATH erkannt (FOR JSON PATH / explicit PATH alias) → Rückgabe "ResultSet{index+1}" unverändert (später erweiterbar für PATH alias).
/// 2. Andernfalls erste Tabellenquelle (Base Table oder View) aus erstem SELECT Statement.
/// 3. Fallback: null (damit bestehende Logik `ResultSet{index+1}` nutzt).
/// </summary>
internal static class ResultSetNameResolver
{
    public static string? TryResolve(int index, string procedureSql)
    {
        if (string.IsNullOrWhiteSpace(procedureSql)) return null;
        try
        {
            var parser = new TSql150Parser(true);
            using var sr = new StringReader(procedureSql);
            var fragment = parser.Parse(sr, out IList<ParseError> errors);
            if (errors != null && errors.Count > 0) return null; // unzuverlässig
            // Suche erstes Select
            var visitor = new FirstSelectVisitor();
            fragment.Accept(visitor);
            if (visitor.FirstSelect == null) return null;
            // Prüfe JSON (FOR JSON ...)
            if (visitor.FirstSelect.ForClause != null)
            {
                // Placeholder for future differentiated naming (e.g. from path); currently no alternative name.
                return null; // Beibehaltung bestehender Fallback-Konvention
            }
            // Tabellenquellen inspizieren
            var table = TryGetFirstBaseTableFromSelect(visitor.FirstSelect);
            if (!string.IsNullOrWhiteSpace(table)) return table;
        }
        catch { /* Silent fallback */ }
        return null;
    }

    private static string? TryGetFirstBaseTableFromSelect(QuerySpecification qs)
    {
        foreach (var from in qs.FromClause?.TableReferences ?? new List<TableReference>())
        {
            if (from is NamedTableReference ntr)
            {
                return ntr.SchemaObject.BaseIdentifier?.Value;
            }
        }
        return null;
    }

    private sealed class FirstSelectVisitor : TSqlFragmentVisitor
    {
        public QuerySpecification? FirstSelect { get; private set; }
        public override void ExplicitVisit(QuerySpecification node)
        {
            if (FirstSelect == null) FirstSelect = node;
            // Do not traverse deeper — sufficient.
        }
    }
}
