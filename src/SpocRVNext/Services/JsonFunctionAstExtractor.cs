using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.SpocRVNext.Services;

// Finale saubere Implementierung
public sealed class JsonFunctionAstExtractor
{
    private sealed class FastReader : TextReader
    { private readonly string _t; private int _p; public FastReader(string t) => _t = t ?? string.Empty; public override int Read(char[] b, int i, int c) { if (_p >= _t.Length) return 0; int k = Math.Min(c, _t.Length - _p); _t.CopyTo(_p, b, i, k); _p += k; return k; } public override int Read() => _p >= _t.Length ? -1 : _t[_p++]; }
    private sealed class QsCollectorVisitor : TSqlFragmentVisitor
    { private readonly Action<QuerySpecification> _on; public QsCollectorVisitor(Action<QuerySpecification> on) => _on = on; public override void Visit(TSqlFragment f) { if (f is QuerySpecification qs) _on(qs); base.Visit(f); } }

    public JsonFunctionAstResult Parse(string sql)
    {
        var res = new JsonFunctionAstResult(); if (string.IsNullOrWhiteSpace(sql)) return res;
        var parser = new TSql160Parser(false); using var reader = new FastReader(sql); var fragment = parser.Parse(reader, out var errs); if (errs?.Count > 0) res.Errors.AddRange(errs.Select(e => e.Message));
        var specs = new List<QuerySpecification>(); fragment.Accept(new QsCollectorVisitor(q => specs.Add(q)));
        var jsonSpecs = specs.Where(q => GetForJsonClause(q) != null).ToList(); if (jsonSpecs.Count == 0) return res;
        QuerySpecification? root = jsonSpecs.FirstOrDefault(q => RootFragmentHasWithoutArrayWrapper(sql, q));
        root ??= jsonSpecs.OrderByDescending(q => q.SelectElements.OfType<SelectScalarExpression>().Count(se => se.Expression is not ScalarSubquery)).First();
        var forClause = GetForJsonClause(root)!; res.ReturnsJson = true; res.JsonRoot = GetRootName(forClause);
        bool withoutViaProperty = GetWithoutArrayWrapper(forClause);
        bool withoutViaRaw = RootFragmentHasWithoutArrayWrapper(sql, root);
        res.ReturnsJsonArray = !(withoutViaProperty || withoutViaRaw);
        foreach (var se in root.SelectElements.OfType<SelectScalarExpression>())
        {
            var alias = se.ColumnName?.Value ?? InferAlias(se.Expression);
            res.Columns.Add(BuildColumn(se.Expression, alias, 0));
        }
        if (sql.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0 && res.ReturnsJsonArray)
            res.ReturnsJsonArray = false;
        return res;
    }

    private JsonFunctionAstColumn BuildColumn(ScalarExpression expr, string alias, int depth)
    {
        if (depth > 20) return new JsonFunctionAstColumn { Name = alias }; // Sicherheitsgrenze

        JsonFunctionAstColumn Make(string name, bool nested = false, bool returnsJson = false, bool? returnsJsonArray = null)
        {
            var col = new JsonFunctionAstColumn
            {
                Name = name,
                IsNestedJson = nested,
                ReturnsJson = returnsJson,
                ReturnsJsonArray = returnsJsonArray,
                SourceSql = ExtractFragment(expr)
            };
            foreach (var p in ExtractParts(expr)) col.Parts.Add(p);
            return col;
        }

        if (expr is ScalarSubquery ss && ss.QueryExpression is QuerySpecification qs)
        {
            var fc = GetForJsonClause(qs);
            if (fc != null)
            {
                var col = Make(alias, nested: true, returnsJson: true, returnsJsonArray: !GetWithoutArrayWrapper(fc));
                foreach (var inner in qs.SelectElements.OfType<SelectScalarExpression>())
                {
                    var a = inner.ColumnName?.Value ?? InferAlias(inner.Expression);
                    col.Children.Add(BuildColumn(inner.Expression, a, depth + 1));
                }
                return col;
            }
        }
        if (expr is FunctionCall fcCall && fcCall.FunctionName?.Value?.Equals("JSON_QUERY", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Make(alias, nested: true, returnsJson: true);
        }
        return Make(alias);
    }

    private string? ExtractFragment(TSqlFragment frag)
    {
        // Ursprünglich wurde hier ein Ausschnitt aus der ursprünglichen SQL über ein internes Feld _currentSql extrahiert.
        // Das Feld wurde entfernt (war ungenutzt / nicht gesetzt). Für zukünftige Nutzung könnte man das SQL
        // als Parameter durchreichen. Aktuell geben wir keinen Ausschnitt zurück, um Warnungen zu vermeiden.
        return null; // bewusst deaktiviert
    }

    private List<string> ExtractParts(ScalarExpression expr)
    {
        var parts = new List<string>();
        if (expr is ColumnReferenceExpression cr && cr.MultiPartIdentifier != null)
        {
            parts.AddRange(cr.MultiPartIdentifier.Identifiers.Select(i => i.Value));
        }
        else if (expr is FunctionCall fc)
        {
            parts.Add(fc.FunctionName.Value);
        }
        return parts;
    }

    private string InferAlias(ScalarExpression expr) => expr switch
    {
        ColumnReferenceExpression cr => cr.MultiPartIdentifier?.Identifiers?.LastOrDefault()?.Value ?? "col",
        FunctionCall f => f.FunctionName?.Value ?? "col",
        ScalarSubquery => "col",
        _ => "col"
    };

    private object? GetForJsonClause(QuerySpecification qs) => qs?.GetType().GetProperty("ForClause")?.GetValue(qs);
    private bool GetWithoutArrayWrapper(object forJsonClause)
    { var p = forJsonClause.GetType().GetProperty("WithoutArrayWrapper"); if (p == null) return false; var v = p.GetValue(forJsonClause); return v is bool b && b; }
    private string? GetRootName(object forJsonClause)
    { var p = forJsonClause.GetType().GetProperty("RootName"); if (p == null) return null; var v = p.GetValue(forJsonClause); if (v == null) return null; var vp = v.GetType().GetProperty("Value"); return vp?.GetValue(v) as string; }
    private bool RootFragmentHasWithoutArrayWrapper(string sql, QuerySpecification root)
    {
        if (root.StartOffset >= 0 && root.FragmentLength > 0 && root.StartOffset + root.FragmentLength <= sql.Length)
        {
            var frag = sql.Substring(root.StartOffset, root.FragmentLength);
            return frag.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        return sql.IndexOf("WITHOUT_ARRAY_WRAPPER", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

public sealed class JsonFunctionAstResult
{ public bool ReturnsJson { get; set; } public bool ReturnsJsonArray { get; set; } public string? JsonRoot { get; set; } public List<JsonFunctionAstColumn> Columns { get; } = new(); public List<string> Errors { get; } = new(); }

public sealed class JsonFunctionAstColumn
{
    public string Name { get; set; } = string.Empty;
    public bool IsNestedJson { get; set; }
    public bool ReturnsJson { get; set; }
    public bool? ReturnsJsonArray { get; set; }
    public List<JsonFunctionAstColumn> Children { get; } = new();
    // Zusätzliche Metadaten zur besseren Auflösung
    public string? SourceSql { get; set; }
    public List<string> Parts { get; } = new();
}