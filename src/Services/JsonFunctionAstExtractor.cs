using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SpocR.Services;

/// <summary>
/// Parses T-SQL function definitions using ScriptDom to extract JSON return metadata and column projections.
/// First iteration: focuses on SELECT ... FOR JSON blocks inside scalar functions.
/// </summary>
public sealed class JsonFunctionAstExtractor
{
    private sealed class InMemoryStringReader : TextReader
    {
        private readonly string _text; private int _pos;
        public InMemoryStringReader(string text) { _text = text ?? string.Empty; }
        public override int Read(char[] buffer, int index, int count) {
            if (_pos >= _text.Length) return 0;
            int toCopy = Math.Min(count, _text.Length - _pos);
            _text.CopyTo(_pos, buffer, index, toCopy); _pos += toCopy; return toCopy;
        }
        public override int Peek() => _pos >= _text.Length ? -1 : _text[_pos];
        public override int Read() => _pos >= _text.Length ? -1 : _text[_pos++];
    }

    public JsonFunctionAstResult Parse(string definition)
    {
        var res = new JsonFunctionAstResult();
        if (string.IsNullOrWhiteSpace(definition)) return res;
        TSqlParser parser = new TSql160Parser(initialQuotedIdentifiers: false);
        IList<ParseError> errors;
        using var reader = new InMemoryStringReader(definition);
        var fragment = parser.Parse(reader, out errors);
        if (errors != null && errors.Count > 0)
        {
            res.Errors = errors.Select(e => e.Message).ToList();
            // Best-effort continue; JSON detection may still succeed.
        }

        // Find top-level SELECT statements with FOR JSON clause (reflection-based to avoid version mismatches)
        var selects = new List<SelectStatement>();
        fragment.Accept(new CollectSelectVisitor(selects));
        foreach (var stmt in selects)
        {
            var querySpec = stmt.QueryExpression as QuerySpecification;
            if (querySpec == null) continue;
            // Try property ForClause
            var fcProp = querySpec.GetType().GetProperty("ForClause");
            object fcVal = null;
            if (fcProp != null) fcVal = fcProp.GetValue(querySpec);
            if (fcVal == null || fcVal.GetType().Name != "ForJsonClause") continue;
            res.ReturnsJson = true;
            // WithoutArrayWrapper via reflection
            var withoutArrProp = fcVal.GetType().GetProperty("WithoutArrayWrapper");
            if (withoutArrProp != null)
            {
                var wv = withoutArrProp.GetValue(fcVal) as bool?;
                res.ReturnsJsonArray = !(wv ?? false);
            }
            else res.ReturnsJsonArray = true;
            var rootNameProp = fcVal.GetType().GetProperty("RootName");
            if (rootNameProp != null)
            {
                var rootId = rootNameProp.GetValue(fcVal);
                if (rootId != null)
                {
                    var valProp = rootId.GetType().GetProperty("Value");
                    if (valProp != null)
                    {
                        res.JsonRoot = valProp.GetValue(rootId) as string;
                    }
                }
            }

            // Projection Columns
            foreach (var selectElement in querySpec.SelectElements)
            {
                if (selectElement is SelectScalarExpression scalar)
                {
                    var alias = scalar.ColumnName?.Value ?? InferAliasFromExpression(scalar.Expression);
                    bool nested = ContainsNestedForJsonText(scalar.Expression);
                    var col = new JsonFunctionAstColumn
                    {
                        Name = alias,
                        IsNestedJson = nested,
                        ReturnsJson = nested,
                        ReturnsJsonArray = null // nested array detection later
                    };
                    res.Columns.Add(col);
                }
            }
            // Only first FOR JSON SELECT considered for now
            break;
        }
        return res;
    }

    private static string InferAliasFromExpression(ScalarExpression expr)
    {
        if (expr is ColumnReferenceExpression colRef)
        {
            var last = colRef.MultiPartIdentifier?.Identifiers?.LastOrDefault();
            return last?.Value ?? "col";
        }
        if (expr is FunctionCall fn)
        {
            return fn.FunctionName.Value;
        }
        return "col";
    }

    private static bool ContainsNestedForJsonText(ScalarExpression expr)
    {
        // Fallback heuristic: walk identifiers & function calls to find nested SELECT FOR JSON textual fragments.
        // ScriptDom doesn't easily provide reconstructed text; attempt via fragment script generation could be added later.
        var found = false;
        expr.Accept(new GenericVisitor(f =>
        {
            var typeName = f.GetType().Name;
            if (typeName.Contains("Json") || typeName.Contains("ForJson")) found = true;
        }));
        return found;
    }

    private sealed class CollectSelectVisitor : TSqlFragmentVisitor
    {
        private readonly List<SelectStatement> _target;
        public CollectSelectVisitor(List<SelectStatement> target) { _target = target; }
        public override void Visit(SelectStatement node) => _target.Add(node);
    }

    private sealed class GenericVisitor : TSqlFragmentVisitor
    {
        private readonly Action<TSqlFragment> _on;
        public GenericVisitor(Action<TSqlFragment> on) { _on = on; }
        public override void Visit(TSqlFragment node) { _on(node); base.Visit(node); }
    }
}

public sealed class JsonFunctionAstResult
{
    public bool ReturnsJson { get; set; }
    public bool ReturnsJsonArray { get; set; }
    public string JsonRoot { get; set; }
    public List<JsonFunctionAstColumn> Columns { get; } = new();
    public List<string> Errors { get; set; } = new();
}

public sealed class JsonFunctionAstColumn
{
    public string Name { get; set; }
    public bool IsNestedJson { get; set; }
    public bool ReturnsJson { get; set; }
    public bool? ReturnsJsonArray { get; set; }
    public List<JsonFunctionAstColumn> Children { get; } = new();
}
