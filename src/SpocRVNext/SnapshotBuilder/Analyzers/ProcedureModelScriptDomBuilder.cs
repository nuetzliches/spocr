using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using ProcedureReferenceModel = SpocR.SpocRVNext.SnapshotBuilder.Models.ProcedureReference;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Builds <see cref="ProcedureModel"/> instances directly from ScriptDom ASTs without relying on the legacy <see cref="StoredProcedureContentModel"/>.
/// </summary>
internal sealed class ProcedureModelScriptDomBuilder : IProcedureModelBuilder
{
    public ProcedureModel? Build(string? definition, string? defaultSchema, bool verboseParsing)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        var fragment = ProcedureModelScriptDomParser.Parse(definition);
        if (fragment == null)
        {
            return null;
        }

        var model = new ProcedureModel();
        var visitor = new ProcedureVisitor(defaultSchema);
        fragment.Accept(visitor);

        model.ExecutedProcedures.AddRange(visitor.ExecutedProcedures);
        model.ResultSets.AddRange(visitor.ResultSets);

        return model;
    }

    private sealed class ProcedureVisitor : TSqlFragmentVisitor
    {
        private readonly string? _defaultSchema;
        private readonly List<ProcedureExecutedProcedureCall> _executedProcedures = new();
        private readonly List<ProcedureResultSet> _resultSets = new();
        private bool _inTopLevelSelect;

        public ProcedureVisitor(string? defaultSchema)
        {
            _defaultSchema = string.IsNullOrWhiteSpace(defaultSchema) ? null : defaultSchema;
        }

        public IReadOnlyList<ProcedureExecutedProcedureCall> ExecutedProcedures => _executedProcedures;
        public IReadOnlyList<ProcedureResultSet> ResultSets => _resultSets;

        public override void ExplicitVisit(ExecuteSpecification node)
        {
            if (node?.ExecutableEntity is ExecutableProcedureReference procedureRef)
            {
                var call = MapProcedureReference(procedureRef.ProcedureReference?.ProcedureReference?.Name);
                if (call != null)
                {
                    _executedProcedures.Add(call);
                }
            }

            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(QuerySpecification node)
        {
            var wasTopLevel = _inTopLevelSelect;
            if (!_inTopLevelSelect)
            {
                _inTopLevelSelect = true;
                _resultSets.Add(BuildResultSet(node));
            }

            base.ExplicitVisit(node);
            _inTopLevelSelect = wasTopLevel;
        }

        private ProcedureExecutedProcedureCall? MapProcedureReference(MultiPartIdentifier? identifier)
        {
            if (identifier == null || identifier.Identifiers.Count == 0)
            {
                return null;
            }

            var name = identifier.Identifiers[^1].Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string? schema = null;
            if (identifier.Identifiers.Count >= 2)
            {
                var schemaIdentifier = identifier.Identifiers[^2].Value;
                schema = string.IsNullOrWhiteSpace(schemaIdentifier) ? null : schemaIdentifier;
            }

            schema ??= _defaultSchema;

            return new ProcedureExecutedProcedureCall
            {
                Schema = schema,
                Name = name,
                IsCaptured = true
            };
        }

        private ProcedureResultSet BuildResultSet(QuerySpecification query)
        {
            var resultSet = new ProcedureResultSet();

            foreach (var element in query.SelectElements)
            {
                if (element is SelectScalarExpression scalar)
                {
                    var column = BuildColumnFromScalar(scalar);
                    if (column != null)
                    {
                        resultSet.Columns.Add(column);
                    }
                }
                else if (element is SelectStarExpression star)
                {
                    resultSet.HasSelectStar = true;
                    if (!string.IsNullOrWhiteSpace(star.Qualifier?.Identifiers[^1].Value))
                    {
                        resultSet.Columns.Add(new ProcedureResultColumn
                        {
                            Name = star.Qualifier.Identifiers[^1].Value,
                            ExpressionKind = ProcedureResultColumnExpressionKind.ColumnRef
                        });
                    }
                }
            }

            return resultSet;
        }

        private ProcedureResultColumn? BuildColumnFromScalar(SelectScalarExpression scalar)
        {
            if (scalar == null)
            {
                return null;
            }

            var column = new ProcedureResultColumn
            {
                Name = scalar.ColumnName?.Value,
                RawExpression = scalar.Expression?.ToString()
            };

            switch (scalar.Expression)
            {
                case ColumnReferenceExpression columnRef:
                    PopulateColumnReference(column, columnRef);
                    break;
                case FunctionCall functionCall:
                    PopulateFunctionCall(column, functionCall);
                    break;
                case CastCall castCall:
                    PopulateCast(column, castCall);
                    break;
                case Literal literal:
                    PopulateLiteral(column, literal);
                    break;
            }

            return column;
        }

        private void PopulateLiteral(ProcedureResultColumn column, Literal literal)
        {
            column.ExpressionKind = ProcedureResultColumnExpressionKind.Computed;
            switch (literal)
            {
                case IntegerLiteral:
                    column.HasIntegerLiteral = true;
                    break;
                case NumericLiteral:
                    column.HasDecimalLiteral = true;
                    break;
            }
        }

        private void PopulateCast(ProcedureResultColumn column, CastCall castCall)
        {
            column.ExpressionKind = ProcedureResultColumnExpressionKind.Cast;
            column.CastTargetType = castCall?.DataType?.Name?.BaseIdentifier?.Value;

            if (castCall?.Parameter != null)
            {
                var inner = BuildColumnFromScalar(new SelectScalarExpression
                {
                    Expression = castCall.Parameter,
                    ColumnName = string.IsNullOrWhiteSpace(column.Name)
                        ? null
                        : new IdentifierOrValueExpression { Identifier = new Identifier { Value = column.Name } }
                });
                if (inner != null)
                {
                    column.SourceSchema = inner.SourceSchema;
                    column.SourceTable = inner.SourceTable;
                    column.SourceColumn = inner.SourceColumn;
                    column.Reference = inner.Reference;
                }
            }
        }

        private void PopulateFunctionCall(ProcedureResultColumn column, FunctionCall functionCall)
        {
            column.ExpressionKind = ProcedureResultColumnExpressionKind.FunctionCall;

            string? schema = null;
            var name = functionCall?.FunctionName?.Value;

            if (functionCall?.CallTarget is MultiPartIdentifierCallTarget fnTarget && fnTarget.MultiPartIdentifier != null && fnTarget.MultiPartIdentifier.Identifiers.Count > 0)
            {
                schema = fnTarget.MultiPartIdentifier.Identifiers[^1].Value;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                column.Reference = new ProcedureReferenceModel
                {
                    Kind = "Function",
                    Schema = schema,
                    Name = name
                };
            }

            if (functionCall?.CallTarget is MultiPartIdentifierCallTarget target && target.MultiPartIdentifier != null)
            {
                column.SourceSchema = target.MultiPartIdentifier.Identifiers.Count >= 2
                    ? target.MultiPartIdentifier.Identifiers[^2].Value
                    : null;
                column.SourceTable = target.MultiPartIdentifier.Identifiers.Count >= 1
                    ? target.MultiPartIdentifier.Identifiers[^1].Value
                    : null;
            }
        }

        private void PopulateColumnReference(ProcedureResultColumn column, ColumnReferenceExpression columnRef)
        {
            column.ExpressionKind = ProcedureResultColumnExpressionKind.ColumnRef;
            if (columnRef?.MultiPartIdentifier == null || columnRef.MultiPartIdentifier.Count == 0)
            {
                return;
            }

            var identifiers = columnRef.MultiPartIdentifier.Identifiers;
            column.SourceColumn = identifiers[^1].Value;

            if (identifiers.Count >= 2)
            {
                column.SourceTable = identifiers[^2].Value;
            }

            if (identifiers.Count >= 3)
            {
                column.SourceSchema = identifiers[^3].Value;
            }
        }
    }
}
