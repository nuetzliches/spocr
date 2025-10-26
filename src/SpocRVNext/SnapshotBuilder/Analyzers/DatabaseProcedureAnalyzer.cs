using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Models;
using SpocR.DataContext.Queries;
using SpocR.Models;
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

    public DatabaseProcedureAnalyzer(DbContext dbContext, IConsoleService console, IDependencyMetadataProvider dependencyMetadataProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _dependencyMetadataProvider = dependencyMetadataProvider ?? throw new ArgumentNullException(nameof(dependencyMetadataProvider));
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

        StoredProcedureContentModel.SetAstVerbose(options?.Verbose ?? false);

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

            StoredProcedureContentModel? ast = null;
            if (!string.IsNullOrWhiteSpace(descriptor.Schema) && !string.IsNullOrWhiteSpace(descriptor.Name))
            {
                try
                {
                    var definition = await _dbContext.StoredProcedureContentAsync(descriptor.Schema, descriptor.Name, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(definition))
                    {
                        ast = StoredProcedureContentModel.Parse(definition, descriptor.Schema);
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

            if (ast != null)
            {
                // Explicit EXEC dependencies (resolved by AST resolver)
                foreach (var exec in ast.ExecutedProcedures ?? Array.Empty<StoredProcedureContentModel.ExecutedProcedureCall>())
                {
                    AddDependency(ProcedureDependencyKind.Procedure, exec?.Schema, exec?.Name);
                }

                // Result-set level references (cross-schema forwarding)
                foreach (var rs in ast.ResultSets ?? Array.Empty<StoredProcedureContentModel.ResultSet>())
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
                Ast = ast,
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
        IReadOnlyList<StoredProcedureContentModel.ResultColumn> columns,
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
}
