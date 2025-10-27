using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Re-parses procedure definitions to capture EXEC dependencies independently from the legacy model.
/// </summary>
internal static class ProcedureModelExecAnalyzer
{
    private sealed record ExecutedProcedure(string? Schema, string Name);

    public static void Apply(string? definition, ProcedureModel? model)
    {
        if (string.IsNullOrWhiteSpace(definition) || model == null)
        {
            return;
        }

        TSqlFragment fragment;
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using (var reader = new StringReader(definition))
        {
            fragment = parser.Parse(reader, out _);
        }

        if (fragment == null)
        {
            return;
        }

        var visitor = new ExecVisitor();
        fragment.Accept(visitor);

        if (visitor.ExecutedProcedures.Count == 0)
        {
            model.ExecutedProcedures.Clear();
            return;
        }

        model.ExecutedProcedures.Clear();
        foreach (var executed in visitor.ExecutedProcedures)
        {
            model.ExecutedProcedures.Add(new ProcedureExecutedProcedureCall
            {
                Schema = executed.Schema,
                Name = executed.Name,
                IsCaptured = true
            });
        }
    }

    private sealed class ExecVisitor : TSqlFragmentVisitor
    {
        private readonly Dictionary<string, ExecutedProcedure> _map = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<ExecutedProcedure> ExecutedProcedures => _map.Values;

        public override void ExplicitVisit(ExecuteSpecification node)
        {
            if (node == null)
            {
                return;
            }

            if (node.ExecutableEntity is ExecutableProcedureReference procedureRef)
            {
                CaptureProcedure(procedureRef);
            }

            base.ExplicitVisit(node);
        }

        private void CaptureProcedure(ExecutableProcedureReference procedureRef)
        {
            var schemaObject = procedureRef?.ProcedureReference?.ProcedureReference?.Name;
            if (schemaObject == null || schemaObject.Identifiers == null || schemaObject.Identifiers.Count == 0)
            {
                return;
            }

            var procedureIdentifier = schemaObject.Identifiers[^1];
            if (procedureIdentifier == null || string.IsNullOrWhiteSpace(procedureIdentifier.Value))
            {
                return;
            }

            var procedureName = procedureIdentifier.Value;
            string? schema = null;
            if (schemaObject.Identifiers.Count >= 2)
            {
                var schemaIdentifier = schemaObject.Identifiers[^2];
                if (schemaIdentifier != null && !string.IsNullOrWhiteSpace(schemaIdentifier.Value))
                {
                    schema = schemaIdentifier.Value;
                }
            }

            var key = string.IsNullOrWhiteSpace(schema)
                ? procedureName.ToLowerInvariant()
                : string.Concat(schema.ToLowerInvariant(), ".", procedureName.ToLowerInvariant());

            if (!_map.ContainsKey(key))
            {
                _map[key] = new ExecutedProcedure(schema, procedureName);
            }
        }
    }
}
