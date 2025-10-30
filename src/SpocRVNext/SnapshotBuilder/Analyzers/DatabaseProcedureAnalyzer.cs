using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Data.Queries;
using SpocR.SpocRVNext.Models;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Metadata;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.SnapshotBuilder.Analyzers;

/// <summary>
/// Analyzer that pulls procedure metadata from the database, parses the SQL definition, and extracts dependency information.
/// </summary>
internal sealed class DatabaseProcedureAnalyzer : IProcedureAnalyzer
{
    private readonly DbContext _dbContext;
    private readonly IConsoleService _console;
    private readonly IDependencyMetadataProvider _dependencyMetadataProvider;
    private readonly IFunctionJsonMetadataProvider _functionJsonMetadataProvider;
    private readonly IProcedureModelBuilder _procedureModelBuilder;

    public DatabaseProcedureAnalyzer(
        DbContext dbContext,
        IConsoleService console,
        IDependencyMetadataProvider dependencyMetadataProvider,
        IFunctionJsonMetadataProvider functionJsonMetadataProvider,
        IProcedureModelBuilder procedureModelBuilder)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _dependencyMetadataProvider = dependencyMetadataProvider ?? throw new ArgumentNullException(nameof(dependencyMetadataProvider));
        _functionJsonMetadataProvider = functionJsonMetadataProvider ?? throw new ArgumentNullException(nameof(functionJsonMetadataProvider));
        _procedureModelBuilder = procedureModelBuilder ?? throw new ArgumentNullException(nameof(procedureModelBuilder));
    }

    public async Task<IReadOnlyList<ProcedureAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<ProcedureCollectionItem> items,
        SnapshotBuildOptions options,
        CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
        {
            return Array.Empty<ProcedureAnalysisResult>();
        }

        var results = new List<ProcedureAnalysisResult>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var descriptor = item?.Descriptor ?? new ProcedureDescriptor();
            var snapshotFile = item?.CachedSnapshotFile ?? BuildSnapshotFileName(descriptor);
            var dependencies = new Dictionary<string, ProcedureDependency>(StringComparer.OrdinalIgnoreCase);

            void AddDependency(ProcedureDependencyKind kind, string? schema, string? name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                var effectiveSchema = schema;
                if (string.IsNullOrWhiteSpace(effectiveSchema) && kind == ProcedureDependencyKind.Procedure)
                {
                    effectiveSchema = descriptor.Schema;
                }
                effectiveSchema ??= string.Empty;
                var normalizedSchema = effectiveSchema;
                var key = $"{kind}|{normalizedSchema}|{name}";
                if (!dependencies.ContainsKey(key))
                {
                    dependencies[key] = new ProcedureDependency
                    {
                        Kind = kind,
                        Schema = normalizedSchema,
                        Name = name
                    };
                }
            }

            // Gather table type dependencies via parameter metadata
            List<StoredProcedureInput>? parameters = null;
            if (!string.IsNullOrWhiteSpace(descriptor.Schema) && !string.IsNullOrWhiteSpace(descriptor.Name))
            {
                try
                {
                    parameters = await _dbContext.StoredProcedureInputListAsync(descriptor.Schema, descriptor.Name, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-analyze] Failed to load parameters for {descriptor}: {ex.Message}");
                }
            }

            if (parameters != null)
            {
                foreach (var input in parameters)
                {
                    if (input == null) continue;

                    if (input.IsTableType)
                    {
                        AddDependency(ProcedureDependencyKind.UserDefinedTableType, input.UserTypeSchemaName, input.UserTypeName);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(input.UserTypeSchemaName) && !string.IsNullOrWhiteSpace(input.UserTypeName))
                    {
                        AddDependency(ProcedureDependencyKind.UserDefinedType, input.UserTypeSchemaName, input.UserTypeName);
                    }
                }
            }

            var parameterSnapshot = parameters?.Count > 0 ? parameters.ToList() : new List<StoredProcedureInput>();

            string? definition = null;
            ProcedureModel? procedureModel = null;
            if (!string.IsNullOrWhiteSpace(descriptor.Schema) && !string.IsNullOrWhiteSpace(descriptor.Name))
            {
                try
                {
                    definition = await _dbContext.StoredProcedureContentAsync(descriptor.Schema, descriptor.Name, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(definition))
                    {
                        procedureModel = _procedureModelBuilder.Build(definition, descriptor.Schema, options?.Verbose ?? false);
                        if (procedureModel != null)
                        {
                            var scriptDom = ProcedureModelScriptDomParser.Parse(definition);
                            ProcedureModelExecAnalyzer.Apply(scriptDom, procedureModel);
                            ProcedureModelAggregateAnalyzer.Apply(scriptDom, procedureModel);
                            ProcedureModelJsonAnalyzer.Apply(scriptDom, procedureModel, definition);
                            ProcedureModelPostProcessor.Apply(procedureModel);
                        }
                    }
                    else
                    {
                        _console.Verbose($"[snapshot-analyze] No definition found for {descriptor}");
                    }
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-analyze] Failed to parse {descriptor}: {ex.Message}");
                }
            }

            if (procedureModel != null)
            {
                try
                {
                    await EnrichJsonResultSetMetadataAsync(descriptor, procedureModel, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-analyze] JSON enrichment failed for {descriptor}: {ex.Message}");
                }

                // Explicit EXEC dependencies (resolved by AST resolver)
                foreach (var exec in procedureModel.ExecutedProcedures)
                {
                    AddDependency(ProcedureDependencyKind.Procedure, exec?.Schema, exec?.Name);
                }

                // Result-set level references (cross-schema forwarding)
                foreach (var rs in procedureModel.ResultSets)
                {
                    if (!string.IsNullOrWhiteSpace(rs.ExecSourceProcedureName))
                    {
                        AddDependency(ProcedureDependencyKind.Procedure, rs.ExecSourceSchemaName, rs.ExecSourceProcedureName);
                    }
                    CollectColumnDependencies(rs.Columns, AddDependency);
                }
            }

            var dependencyList = dependencies.Values
                .OrderBy(d => d.Kind)
                .ThenBy(d => d.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var enrichedDependencies = await _dependencyMetadataProvider.ResolveAsync(dependencyList, cancellationToken).ConfigureAwait(false);

            results.Add(new ProcedureAnalysisResult
            {
                Descriptor = descriptor,
                Procedure = procedureModel,
                WasReusedFromCache = false,
                SourceLastModifiedUtc = item?.LastModifiedUtc,
                SnapshotFile = snapshotFile,
                Parameters = parameterSnapshot,
                Dependencies = enrichedDependencies
            });
        }

        return results;
    }

    private static void CollectColumnDependencies(
        IReadOnlyList<ProcedureResultColumn> columns,
        Action<ProcedureDependencyKind, string?, string?> add)
    {
        if (columns == null || columns.Count == 0) return;

        foreach (var column in columns)
        {
            if (column == null) continue;

            if (column.Reference != null && !string.IsNullOrWhiteSpace(column.Reference.Name))
            {
                var kind = column.Reference.Kind?.ToLowerInvariant() switch
                {
                    "procedure" => ProcedureDependencyKind.Procedure,
                    "function" => ProcedureDependencyKind.Function,
                    "view" => ProcedureDependencyKind.View,
                    "table" => ProcedureDependencyKind.Table,
                    _ => ProcedureDependencyKind.Unknown
                };
                if (kind != ProcedureDependencyKind.Unknown)
                {
                    add(kind, column.Reference.Schema, column.Reference.Name);
                }
            }

            if (column.Columns != null && column.Columns.Count > 0)
            {
                CollectColumnDependencies(column.Columns, add);
            }
        }
    }

    private static string BuildSnapshotFileName(ProcedureDescriptor descriptor)
    {
        var schema = NameSanitizer.SanitizeForFile(descriptor?.Schema ?? string.Empty);
        var name = NameSanitizer.SanitizeForFile(descriptor?.Name ?? string.Empty);
        if (string.IsNullOrWhiteSpace(schema))
        {
            return string.IsNullOrWhiteSpace(name) ? "procedure.json" : $"{name}.json";
        }

        return $"{schema}.{name}.json";
    }

    private async Task EnrichJsonResultSetMetadataAsync(ProcedureDescriptor descriptor, ProcedureModel procedure, CancellationToken cancellationToken)
    {
        if (procedure?.ResultSets == null || procedure.ResultSets.Count == 0) return;

        var tableCache = new Dictionary<string, Dictionary<string, Column>>(StringComparer.OrdinalIgnoreCase);
        var unresolvedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptorLabel = FormatProcedureLabel(descriptor);

        foreach (var resultSet in procedure.ResultSets)
        {
            if (resultSet == null) continue;
            if (!resultSet.ReturnsJson && !(resultSet.Columns?.Any(c => c?.IsNestedJson == true || c?.ReturnsJson == true) ?? false))
            {
                continue;
            }

            if (resultSet.Columns == null || resultSet.Columns.Count == 0)
            {
                continue;
            }

            foreach (var column in resultSet.Columns)
            {
                var initialPath = column?.Name;
                await EnrichColumnRecursiveAsync(column, tableCache, descriptorLabel, initialPath, unresolvedColumns, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnrichColumnRecursiveAsync(
        ProcedureResultColumn? column,
        Dictionary<string, Dictionary<string, Column>> tableCache,
        string descriptorLabel,
        string? path,
        ISet<string> unresolvedColumns,
        CancellationToken cancellationToken)
    {
        if (column == null) return;

        var currentPath = string.IsNullOrWhiteSpace(path) ? column.Name : path;

        if (column.Columns != null && column.Columns.Count > 0)
        {
            foreach (var child in column.Columns)
            {
                var childPath = CombinePath(currentPath, child?.Name);
                await EnrichColumnRecursiveAsync(child, tableCache, descriptorLabel, childPath, unresolvedColumns, cancellationToken).ConfigureAwait(false);
            }
        }

        if (column.Reference != null && string.Equals(column.Reference.Kind, "Function", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyFunctionJsonMetadataAsync(column, cancellationToken).ConfigureAwait(false);
        }

        if (column.ReturnsJson == true) return;

        var metadata = await ResolveColumnMetadataAsync(column, tableCache, cancellationToken).ConfigureAwait(false);
        if (metadata == null)
        {
            var warningPath = !string.IsNullOrWhiteSpace(currentPath)
                ? currentPath
                : !string.IsNullOrWhiteSpace(column.Name) ? column.Name : "(unnamed)";

            if (!string.IsNullOrWhiteSpace(warningPath) && unresolvedColumns.Add(string.Concat(descriptorLabel, "|", warningPath)))
            {
                var sourceDetails = BuildColumnSourceDetails(column);
                _console.Warn($"[snapshot-analyze] Type resolution failed for column '{warningPath}' in {descriptorLabel}{sourceDetails}. Snapshot will omit sqlType metadata.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(metadata.SqlType))
        {
            column.SqlTypeName = metadata.SqlType;
        }
        if (!column.MaxLength.HasValue && metadata.MaxLength.HasValue)
        {
            column.MaxLength = metadata.MaxLength;
        }
        if (!column.IsNullable.HasValue && metadata.IsNullable.HasValue)
        {
            column.IsNullable = metadata.IsNullable;
        }
        if (!string.IsNullOrWhiteSpace(metadata.UserTypeSchema) && string.IsNullOrWhiteSpace(column.UserTypeSchemaName))
        {
            column.UserTypeSchemaName = metadata.UserTypeSchema;
        }
        if (!string.IsNullOrWhiteSpace(metadata.UserTypeName) && string.IsNullOrWhiteSpace(column.UserTypeName))
        {
            column.UserTypeName = metadata.UserTypeName;
        }
    }

    private async Task ApplyFunctionJsonMetadataAsync(ProcedureResultColumn column, CancellationToken cancellationToken)
    {
        if (column?.Reference == null) return;

        var name = column.Reference.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        var schema = string.IsNullOrWhiteSpace(column.Reference.Schema) ? null : column.Reference.Schema;
        var metadata = await _functionJsonMetadataProvider.ResolveAsync(schema, name, cancellationToken).ConfigureAwait(false);
        if (metadata == null || !metadata.ReturnsJson) return;

        column.ReturnsJson = true;
        column.IsNestedJson = column.IsNestedJson ?? true;
        column.ReturnsJsonArray = metadata.ReturnsJsonArray;

        if (string.IsNullOrWhiteSpace(column.JsonRootProperty) && !string.IsNullOrWhiteSpace(metadata.RootProperty))
        {
            column.JsonRootProperty = metadata.RootProperty;
        }
    }

    private async Task<ColumnMetadata?> ResolveColumnMetadataAsync(
        ProcedureResultColumn column,
        Dictionary<string, Dictionary<string, Column>> tableCache,
        CancellationToken cancellationToken)
    {
        if (column == null) return null;
        Column? tableColumn = null;
        if (!string.IsNullOrWhiteSpace(column.SourceSchema) && !string.IsNullOrWhiteSpace(column.SourceTable) && !string.IsNullOrWhiteSpace(column.SourceColumn))
        {
            tableColumn = await GetTableColumnAsync(column.SourceSchema, column.SourceTable, column.SourceColumn, tableCache, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(column.CastTargetType))
        {
            var normalized = NormalizeSqlType(column.CastTargetType);
            var maxLen = NormalizeLength(column.CastTargetLength);
            var precision = column.CastTargetPrecision;
            var scale = column.CastTargetScale;
            var userSchema = !string.IsNullOrWhiteSpace(column.UserTypeSchemaName) ? column.UserTypeSchemaName : tableColumn?.UserTypeSchemaName;
            var userName = !string.IsNullOrWhiteSpace(column.UserTypeName) ? column.UserTypeName : tableColumn?.UserTypeName;
            var isNullable = column.IsNullable ?? tableColumn?.IsNullable;
            return new ColumnMetadata(normalized, maxLen, precision, scale, isNullable, userSchema, userName);
        }

        if (tableColumn != null)
        {
            var tableMaxLength = NormalizeLength(tableColumn.MaxLength);
            var tablePrecision = NormalizePrecision(tableColumn.Precision);
            var tableScale = NormalizePrecision(tableColumn.Scale);
            var baseType = !string.IsNullOrWhiteSpace(tableColumn.BaseSqlTypeName) ? tableColumn.BaseSqlTypeName : tableColumn.SqlTypeName;
            var formatted = FormatSqlType(baseType, tableMaxLength, tablePrecision, tableScale);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                formatted = !string.IsNullOrWhiteSpace(column.SqlTypeName) ? NormalizeSqlType(column.SqlTypeName) : baseType?.Trim();
            }

            var effectiveSqlType = !string.IsNullOrWhiteSpace(formatted) ? formatted : (!string.IsNullOrWhiteSpace(column.SqlTypeName) ? NormalizeSqlType(column.SqlTypeName) : null);
            if (string.IsNullOrWhiteSpace(effectiveSqlType) && !string.IsNullOrWhiteSpace(baseType))
            {
                effectiveSqlType = baseType.Trim();
            }

            var effectiveMaxLength = column.MaxLength ?? tableMaxLength;
            var effectiveNullable = column.IsNullable ?? tableColumn.IsNullable;
            var effectivePrecision = column.CastTargetPrecision ?? tablePrecision;
            var effectiveScale = column.CastTargetScale ?? tableScale;
            var userSchema = !string.IsNullOrWhiteSpace(column.UserTypeSchemaName) ? column.UserTypeSchemaName : tableColumn.UserTypeSchemaName;
            var userName = !string.IsNullOrWhiteSpace(column.UserTypeName) ? column.UserTypeName : tableColumn.UserTypeName;

            return new ColumnMetadata(
                effectiveSqlType ?? string.Empty,
                effectiveMaxLength,
                effectivePrecision,
                effectiveScale,
                effectiveNullable,
                userSchema,
                userName);
        }

        if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
        {
            var normalized = NormalizeSqlType(column.SqlTypeName);
            return new ColumnMetadata(normalized, column.MaxLength, null, null, column.IsNullable, column.UserTypeSchemaName, column.UserTypeName);
        }

        var heuristic = ResolveHeuristic(column);
        if (heuristic != null)
        {
            return heuristic;
        }

        return null;
    }

    private static string? CombinePath(string? parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(child)) return parent;
        if (string.IsNullOrWhiteSpace(parent)) return child;
        return string.Concat(parent, ".", child);
    }

    private static string FormatProcedureLabel(ProcedureDescriptor descriptor)
    {
        if (descriptor == null)
        {
            return "(unknown procedure)";
        }

        var schema = descriptor.Schema?.Trim();
        var name = descriptor.Name?.Trim();

        if (string.IsNullOrWhiteSpace(schema))
        {
            return string.IsNullOrWhiteSpace(name) ? "(unknown procedure)" : name;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return schema;
        }

        return string.Concat(schema, ".", name);
    }

    private static string BuildColumnSourceDetails(ProcedureResultColumn column)
    {
        if (column == null) return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(column.SourceSchema)) parts.Add(column.SourceSchema);
        if (!string.IsNullOrWhiteSpace(column.SourceTable)) parts.Add(column.SourceTable);

        var location = parts.Count > 0 ? string.Join('.', parts) : null;
        if (!string.IsNullOrWhiteSpace(column.SourceColumn))
        {
            location = string.IsNullOrWhiteSpace(location)
                ? column.SourceColumn
                : string.Concat(location, '.', column.SourceColumn);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            return string.Concat(" (source: ", location, ")");
        }

        if (!string.IsNullOrWhiteSpace(column.SourceAlias))
        {
            return string.Concat(" (source alias: ", column.SourceAlias, ")");
        }

        return string.Empty;
    }

    private async Task<Column?> GetTableColumnAsync(
        string schema,
        string table,
        string columnName,
        Dictionary<string, Dictionary<string, Column>> tableCache,
        CancellationToken cancellationToken)
    {
        var key = $"{schema}.{table}";
        if (!tableCache.TryGetValue(key, out var map))
        {
            var list = await _dbContext.TableColumnsListAsync(schema, table, cancellationToken).ConfigureAwait(false) ?? new List<Column>();
            map = list.Where(c => c != null).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            tableCache[key] = map;
        }

        return map.TryGetValue(columnName, out var column) ? column : null;
    }

    private static ColumnMetadata? ResolveHeuristic(ProcedureResultColumn column)
    {
        var aggregate = column.AggregateFunction;
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            aggregate = TryExtractFunctionName(column.RawExpression);
        }
        if (!string.IsNullOrWhiteSpace(aggregate))
        {
            switch (aggregate.Trim().ToLowerInvariant())
            {
                case "count":
                    return new ColumnMetadata("int", null, null, null, column.IsNullable ?? false, null, null);
                case "count_big":
                    return new ColumnMetadata("bigint", null, null, null, column.IsNullable ?? false, null, null);
                case "sum":
                case "avg":
                    return new ColumnMetadata("decimal(18,2)", null, 18, 2, column.IsNullable ?? true, null, null);
                case "min":
                case "max":
                    if (column.HasIntegerLiteral) return new ColumnMetadata("int", null, null, null, column.IsNullable ?? true, null, null);
                    if (column.HasDecimalLiteral) return new ColumnMetadata("decimal(18,2)", null, 18, 2, column.IsNullable ?? true, null, null);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(column.RawExpression))
        {
            var raw = column.RawExpression.Trim();
            if (raw.StartsWith("EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                return new ColumnMetadata("bit", null, null, null, false, null, null);
            }
            if (LooksLikeBooleanCase(raw))
            {
                return new ColumnMetadata("bit", null, null, null, true, null, null);
            }
        }

        return null;
    }

    private static string NormalizeSqlType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant();
    }

    private static int? NormalizeLength(int? value)
    {
        if (!value.HasValue) return null;
        var val = value.Value;
        if (val <= 0) return null;
        if (val == int.MaxValue) return -1;
        return val;
    }

    private static int? NormalizePrecision(int? value)
    {
        if (!value.HasValue) return null;
        var val = value.Value;
        return val <= 0 ? null : val;
    }

    private static string FormatSqlType(string baseType, int? maxLength, int? precision, int? scale)
    {
        if (string.IsNullOrWhiteSpace(baseType)) return string.Empty;
        var normalized = baseType.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "decimal":
            case "numeric":
                if (precision.HasValue)
                {
                    return $"{normalized}({precision.Value},{(scale ?? 0)})";
                }
                return normalized;
            case "varchar":
            case "nvarchar":
            case "varbinary":
            case "char":
            case "nchar":
            case "binary":
                if (maxLength.HasValue)
                {
                    if (maxLength.Value < 0) return $"{normalized}(max)";
                    return $"{normalized}({maxLength.Value})";
                }
                return $"{normalized}(max)";
            case "datetime2":
            case "datetimeoffset":
            case "time":
                if (scale.HasValue)
                {
                    return $"{normalized}({scale.Value})";
                }
                return normalized;
            default:
                return normalized;
        }
    }

    private static bool LooksLikeBooleanCase(string rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression)) return false;
        if (!rawExpression.StartsWith("CASE", StringComparison.OrdinalIgnoreCase)) return false;
        var text = rawExpression;
        var hasThenOneElseZero = text.IndexOf(" THEN 1", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf(" ELSE 0", StringComparison.OrdinalIgnoreCase) >= 0;
        var hasThenZeroElseOne = text.IndexOf(" THEN 0", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf(" ELSE 1", StringComparison.OrdinalIgnoreCase) >= 0;
        return hasThenOneElseZero || hasThenZeroElseOne;
    }

    private static string? TryExtractFunctionName(string? rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression)) return null;
        var match = Regex.Match(rawExpression, "^\\s*([A-Za-z0-9_]+)\\s*\\(");
        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed record ColumnMetadata(
        string SqlType,
        int? MaxLength,
        int? Precision,
        int? Scale,
        bool? IsNullable,
        string? UserTypeSchema,
        string? UserTypeName
    );
}
