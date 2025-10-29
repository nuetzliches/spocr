using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext;
using SpocR.DataContext.Models;
using SpocR.DataContext.Queries;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Metadata;
using SpocR.SpocRVNext.SnapshotBuilder.Models;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal sealed class SchemaArtifactWriter
{
    private readonly IConsoleService _console;
    private readonly DbContext _dbContext;
    private readonly ITableMetadataProvider _tableMetadataProvider;
    private readonly ITableTypeMetadataProvider _tableTypeMetadataProvider;
    private readonly IUserDefinedTypeMetadataProvider _userDefinedTypeMetadataProvider;
    private readonly Func<string, byte[], CancellationToken, Task<ArtifactWriteOutcome>> _artifactWriter;

    public SchemaArtifactWriter(
        IConsoleService console,
        DbContext dbContext,
        ITableMetadataProvider tableMetadataProvider,
        ITableTypeMetadataProvider tableTypeMetadataProvider,
        IUserDefinedTypeMetadataProvider userDefinedTypeMetadataProvider,
        Func<string, byte[], CancellationToken, Task<ArtifactWriteOutcome>> artifactWriter)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tableMetadataProvider = tableMetadataProvider ?? throw new ArgumentNullException(nameof(tableMetadataProvider));
        _tableTypeMetadataProvider = tableTypeMetadataProvider ?? throw new ArgumentNullException(nameof(tableTypeMetadataProvider));
        _userDefinedTypeMetadataProvider = userDefinedTypeMetadataProvider ?? throw new ArgumentNullException(nameof(userDefinedTypeMetadataProvider));
        _artifactWriter = artifactWriter ?? throw new ArgumentNullException(nameof(artifactWriter));
    }

    public async Task<SchemaArtifactSummary> WriteAsync(
        string schemaRoot,
        SnapshotBuildOptions options,
        IReadOnlyList<ProcedureAnalysisResult> updatedProcedures,
        ISet<string> requiredTypeRefs,
        CancellationToken cancellationToken)
    {
        var summary = new SchemaArtifactSummary();
        var schemaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options?.Schemas != null)
        {
            foreach (var schema in options.Schemas)
            {
                if (!string.IsNullOrWhiteSpace(schema))
                {
                    schemaSet.Add(schema);
                }
            }
        }

        if (updatedProcedures != null)
        {
            foreach (var proc in updatedProcedures)
            {
                var schema = proc?.Descriptor?.Schema;
                if (!string.IsNullOrWhiteSpace(schema))
                {
                    schemaSet.Add(schema);
                }
            }
        }

        if (schemaSet.Count == 0)
        {
            return summary;
        }

        var functionSummary = await WriteFunctionArtifactsAsync(schemaRoot, schemaSet, requiredTypeRefs, cancellationToken).ConfigureAwait(false);
        summary.FilesWritten += functionSummary.FilesWritten;
        summary.FilesUnchanged += functionSummary.FilesUnchanged;
        if (functionSummary.FunctionsVersion > 0)
        {
            summary.FunctionsVersion = functionSummary.FunctionsVersion;
        }

        if (functionSummary.Functions.Count > 0)
        {
            summary.Functions.AddRange(functionSummary.Functions);
        }

        var tableSummary = await WriteTableArtifactsAsync(schemaRoot, schemaSet, requiredTypeRefs, cancellationToken).ConfigureAwait(false);
        summary.FilesWritten += tableSummary.FilesWritten;
        summary.FilesUnchanged += tableSummary.FilesUnchanged;
        if (tableSummary.Tables.Count > 0)
        {
            summary.Tables.AddRange(tableSummary.Tables);
        }

        IReadOnlyList<TableTypeMetadata> tableTypes = Array.Empty<TableTypeMetadata>();
        try
        {
            tableTypes = await _tableTypeMetadataProvider.GetTableTypesAsync(schemaSet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-tabletype] metadata provider failed: {ex.Message}");
        }

        var tableTypeRoot = Path.Combine(schemaRoot, "tabletypes");
        Directory.CreateDirectory(tableTypeRoot);
        var validTableTypeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableTypeMetadata in tableTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tableTypeMetadata == null)
            {
                continue;
            }

            var tableType = tableTypeMetadata.TableType;
            if (tableType == null || string.IsNullOrWhiteSpace(tableType.SchemaName) || string.IsNullOrWhiteSpace(tableType.Name))
            {
                continue;
            }

            var columns = tableTypeMetadata.Columns ?? Array.Empty<Column>();
            var jsonBytes = BuildTableTypeJson(tableType, columns, requiredTypeRefs);
            var fileName = SnapshotWriterUtilities.BuildArtifactFileName(tableType.SchemaName, tableType.Name);
            var filePath = Path.Combine(tableTypeRoot, fileName);
            var outcome = await _artifactWriter(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validTableTypeFiles.Add(fileName);
            summary.TableTypes.Add(new IndexTableTypeEntry
            {
                Schema = tableType.SchemaName,
                Name = tableType.Name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        PruneExtraneousFiles(tableTypeRoot, validTableTypeFiles);

        IReadOnlyList<UserDefinedTypeRow> scalarTypes = Array.Empty<UserDefinedTypeRow>();
        try
        {
            scalarTypes = await _userDefinedTypeMetadataProvider.GetUserDefinedTypesAsync(schemaSet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-udt] metadata provider failed: {ex.Message}");
        }

        var scalarRoot = Path.Combine(schemaRoot, "types");
        Directory.CreateDirectory(scalarRoot);
        var validScalarFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterActive = requiredTypeRefs.Count > 0;

        foreach (var type in scalarTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type == null || string.IsNullOrWhiteSpace(type.schema_name) || string.IsNullOrWhiteSpace(type.user_type_name))
            {
                continue;
            }

            var baseKey = SnapshotWriterUtilities.BuildKey(type.schema_name, type.user_type_name);
            var notNullKey = SnapshotWriterUtilities.BuildKey(type.schema_name, "_" + type.user_type_name);
            if (filterActive && !requiredTypeRefs.Contains(baseKey) && !requiredTypeRefs.Contains(notNullKey))
            {
                continue;
            }

            var jsonBytes = BuildScalarTypeJson(type);
            var fileName = SnapshotWriterUtilities.BuildArtifactFileName(type.schema_name, type.user_type_name);
            var filePath = Path.Combine(scalarRoot, fileName);
            var outcome = await _artifactWriter(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validScalarFiles.Add(fileName);
            summary.UserDefinedTypes.Add(new IndexUserDefinedTypeEntry
            {
                Schema = type.schema_name,
                Name = type.user_type_name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        PruneExtraneousFiles(scalarRoot, validScalarFiles);

        summary.TableTypes.Sort((a, b) =>
        {
            var schemaCompare = string.Compare(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase);
            return schemaCompare != 0 ? schemaCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        summary.UserDefinedTypes.Sort((a, b) =>
        {
            var schemaCompare = string.Compare(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase);
            return schemaCompare != 0 ? schemaCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return summary;
    }

    private async Task<TableArtifactSummary> WriteTableArtifactsAsync(
        string schemaRoot,
        ISet<string> schemaSet,
        ISet<string> requiredTypeRefs,
        CancellationToken cancellationToken)
    {
        var summary = new TableArtifactSummary();

        IReadOnlyList<TableMetadata> tableMetadata = Array.Empty<TableMetadata>();
        try
        {
            tableMetadata = await _tableMetadataProvider.GetTablesAsync(schemaSet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-table] metadata provider failed: {ex.Message}");
            return summary;
        }

        if (tableMetadata == null || tableMetadata.Count == 0)
        {
            return summary;
        }

        var tableRoot = Path.Combine(schemaRoot, "tables");
        Directory.CreateDirectory(tableRoot);
        var validTableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var spocrRoot = Directory.GetParent(schemaRoot)?.FullName ?? schemaRoot;
        var tableCacheRoot = Path.Combine(spocrRoot, "cache", "tables");
        Directory.CreateDirectory(tableCacheRoot);
        var validTableCacheFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tableEntry in tableMetadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tableEntry?.Table;
            if (table == null || string.IsNullOrWhiteSpace(table.SchemaName) || string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var columns = tableEntry?.Columns ?? Array.Empty<Column>();
            var jsonBytes = BuildTableJson(table, columns, requiredTypeRefs);
            var fileName = SnapshotWriterUtilities.BuildArtifactFileName(table.SchemaName, table.Name);
            var filePath = Path.Combine(tableRoot, fileName);
            var outcome = await _artifactWriter(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validTableFiles.Add(fileName);
            var cacheBytes = BuildTableCacheJson(table, columns);
            var cachePath = Path.Combine(tableCacheRoot, fileName);
            await SnapshotWriterUtilities.PersistSnapshotAsync(cachePath, cacheBytes, cancellationToken).ConfigureAwait(false);
            validTableCacheFiles.Add(fileName);

            summary.Tables.Add(new IndexTableEntry
            {
                Schema = table.SchemaName,
                Name = table.Name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        PruneExtraneousFiles(tableRoot, validTableFiles);
        PruneExtraneousFiles(tableCacheRoot, validTableCacheFiles);

        summary.Tables.Sort((a, b) =>
        {
            var schemaCompare = string.Compare(a.Schema, b.Schema, StringComparison.OrdinalIgnoreCase);
            return schemaCompare != 0 ? schemaCompare : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return summary;
    }

    private async Task<FunctionArtifactSummary> WriteFunctionArtifactsAsync(
        string schemaRoot,
        ISet<string> schemaSet,
        ISet<string> requiredTypeRefs,
        CancellationToken cancellationToken)
    {
        var summary = new FunctionArtifactSummary();

        List<FunctionRow> functionRows = new();
        try
        {
            var list = await _dbContext.FunctionListAsync(cancellationToken).ConfigureAwait(false);
            if (list != null)
            {
                functionRows = list;
            }
        }
        catch (Exception ex)
        {
            _console.Verbose($"[snapshot-function] failed to enumerate functions: {ex.Message}");
            return summary;
        }

        var functionRoot = Path.Combine(schemaRoot, "functions");
        Directory.CreateDirectory(functionRoot);
        var validFunctionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<FunctionParamRow> parameterRows = new();
        List<FunctionColumnRow> columnRows = new();
        List<FunctionDependencyRow> dependencyRows = new();

        if (functionRows.Count > 0)
        {
            try
            {
                var list = await _dbContext.FunctionParametersAsync(cancellationToken).ConfigureAwait(false);
                if (list != null)
                {
                    parameterRows = list;
                }
            }
            catch (Exception ex)
            {
                _console.Verbose($"[snapshot-function] failed to load function parameters: {ex.Message}");
            }

            try
            {
                var list = await _dbContext.FunctionTvfColumnsAsync(cancellationToken).ConfigureAwait(false);
                if (list != null)
                {
                    columnRows = list;
                }
            }
            catch (Exception ex)
            {
                _console.Verbose($"[snapshot-function] failed to load function columns: {ex.Message}");
            }

            try
            {
                var list = await _dbContext.FunctionDependenciesAsync(cancellationToken).ConfigureAwait(false);
                if (list != null)
                {
                    dependencyRows = list;
                }
            }
            catch (Exception ex)
            {
                _console.Verbose($"[snapshot-function] failed to load function dependencies: {ex.Message}");
            }
        }

        var parameterLookup = parameterRows
            .GroupBy(row => row.object_id)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.ordinal).ToList());

        var columnLookup = columnRows
            .GroupBy(row => row.object_id)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.ordinal).ToList());

        var dependencyLookup = BuildFunctionDependencyLookup(functionRows, dependencyRows);
        var astExtractor = new JsonFunctionAstExtractor();

        foreach (var function in functionRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (function == null || string.IsNullOrWhiteSpace(function.schema_name) || string.IsNullOrWhiteSpace(function.function_name))
            {
                continue;
            }

            var schema = function.schema_name;
            var name = function.function_name;
            schemaSet.Add(schema);

            parameterLookup.TryGetValue(function.object_id, out var rawParameters);
            rawParameters ??= new List<FunctionParamRow>();

            var isTableValued = string.Equals(function.type_code, "IF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(function.type_code, "TF", StringComparison.OrdinalIgnoreCase);

            var returnInfo = ExtractFunctionReturnInfo(rawParameters, isTableValued);
            var parameters = rawParameters
                .Where(row => !IsReturnParameter(row))
                .OrderBy(row => row.ordinal)
                .ToList();

            List<FunctionColumnRow> columns = new();
            if (isTableValued && columnLookup.TryGetValue(function.object_id, out var mappedColumns))
            {
                columns = mappedColumns;
            }

            dependencyLookup.TryGetValue(function.object_id, out var dependencies);
            dependencies ??= new List<string>();

            JsonFunctionAstResult? astResult = null;
            if (!isTableValued && !string.IsNullOrWhiteSpace(function.definition))
            {
                try
                {
                    astResult = astExtractor.Parse(function.definition);
                }
                catch (Exception ex)
                {
                    _console.Verbose($"[snapshot-function] AST parse failed for {schema}.{name}: {ex.Message}");
                }
            }

            var jsonBytes = BuildFunctionJson(
                function,
                parameters,
                columns,
                dependencies,
                returnInfo.SqlType,
                returnInfo.MaxLength,
                returnInfo.IsNullable,
                astResult,
                requiredTypeRefs);

            var fileName = SnapshotWriterUtilities.BuildArtifactFileName(schema, name);
            var filePath = Path.Combine(functionRoot, fileName);
            var outcome = await _artifactWriter(filePath, jsonBytes, cancellationToken).ConfigureAwait(false);
            if (outcome.Wrote)
            {
                summary.FilesWritten++;
            }
            else
            {
                summary.FilesUnchanged++;
            }

            validFunctionFiles.Add(fileName);
            summary.Functions.Add(new IndexFunctionEntry
            {
                Schema = schema,
                Name = name,
                File = fileName,
                Hash = outcome.Hash
            });
        }

        summary.FunctionsVersion = 2;
        PruneExtraneousFiles(functionRoot, validFunctionFiles);

        return summary;
    }

    private static Dictionary<int, List<string>> BuildFunctionDependencyLookup(
        IReadOnlyList<FunctionRow> functions,
        IReadOnlyList<FunctionDependencyRow> dependencies)
    {
        var result = new Dictionary<int, List<string>>();
        if (functions == null || functions.Count == 0 || dependencies == null || dependencies.Count == 0)
        {
            return result;
        }

        var map = new Dictionary<int, (string Schema, string Name)>();
        foreach (var function in functions)
        {
            if (function == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(function.schema_name) || string.IsNullOrWhiteSpace(function.function_name))
            {
                continue;
            }

            map[function.object_id] = (function.schema_name, function.function_name);
        }

        if (map.Count == 0)
        {
            return result;
        }

        foreach (var group in dependencies.GroupBy(d => d.referencing_id))
        {
            if (!map.ContainsKey(group.Key))
            {
                continue;
            }

            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.referenced_id == entry.referencing_id)
                {
                    continue;
                }

                if (!map.TryGetValue(entry.referenced_id, out var target))
                {
                    continue;
                }

                var key = SnapshotWriterUtilities.BuildKey(target.Schema, target.Name);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    refs.Add(key);
                }
            }

            if (refs.Count > 0)
            {
                result[group.Key] = refs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        return result;
    }

    private static FunctionReturnInfo ExtractFunctionReturnInfo(IReadOnlyList<FunctionParamRow> parameters, bool isTableValued)
    {
        if (isTableValued || parameters == null)
        {
            return new FunctionReturnInfo(null, null, null);
        }

        foreach (var parameter in parameters)
        {
            if (parameter == null)
            {
                continue;
            }

            if (!IsReturnParameter(parameter))
            {
                continue;
            }

            var sqlType = SnapshotWriterUtilities.BuildSqlTypeName(parameter);
            int? maxLength = null;
            if (parameter.normalized_length > 0)
            {
                maxLength = parameter.normalized_length;
            }
            else if (parameter.max_length > 0)
            {
                maxLength = parameter.max_length;
            }

            var isNullable = parameter.is_nullable == 1 ? true : (bool?)null;
            return new FunctionReturnInfo(sqlType, maxLength, isNullable);
        }

        return new FunctionReturnInfo(null, null, null);
    }

    private static bool IsReturnParameter(FunctionParamRow parameter)
        => parameter != null && string.IsNullOrWhiteSpace(parameter.param_name);

    private static byte[] BuildFunctionJson(
        FunctionRow function,
        IReadOnlyList<FunctionParamRow> parameters,
        IReadOnlyList<FunctionColumnRow> columns,
        IReadOnlyList<string> dependencies,
        string? returnSqlType,
        int? returnMaxLength,
        bool? returnIsNullable,
        JsonFunctionAstResult? astResult,
        ISet<string>? requiredTypeRefs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            var schema = function?.schema_name ?? string.Empty;
            var name = function?.function_name ?? string.Empty;
            writer.WriteString("Schema", schema);
            writer.WriteString("Name", name);

            var isTableValued = string.Equals(function?.type_code, "IF", StringComparison.OrdinalIgnoreCase)
                || string.Equals(function?.type_code, "TF", StringComparison.OrdinalIgnoreCase);
            if (isTableValued)
            {
                writer.WriteBoolean("IsTableValued", true);
            }

            if (string.IsNullOrWhiteSpace(function?.definition))
            {
                writer.WriteBoolean("IsEncrypted", true);
            }

            if (!string.IsNullOrWhiteSpace(returnSqlType))
            {
                writer.WriteString("ReturnSqlType", returnSqlType);
            }

            if (returnMaxLength.HasValue)
            {
                writer.WriteNumber("ReturnMaxLength", returnMaxLength.Value);
            }

            if (returnIsNullable == true)
            {
                writer.WriteBoolean("ReturnIsNullable", true);
            }

            if (astResult?.ReturnsJson == true)
            {
                writer.WriteBoolean("ReturnsJson", true);
                if (astResult.ReturnsJsonArray)
                {
                    writer.WriteBoolean("ReturnsJsonArray", true);
                }

                if (!string.IsNullOrWhiteSpace(astResult.JsonRoot))
                {
                    writer.WriteString("JsonRootProperty", astResult.JsonRoot);
                }
            }

            if (dependencies != null && dependencies.Count > 0)
            {
                writer.WritePropertyName("Dependencies");
                writer.WriteStartArray();
                foreach (var dep in dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dep))
                    {
                        continue;
                    }

                    writer.WriteStringValue(dep);
                }

                writer.WriteEndArray();
            }

            writer.WritePropertyName("Parameters");
            writer.WriteStartArray();
            if (parameters != null)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter == null)
                    {
                        continue;
                    }

                    var rawName = parameter.param_name ?? string.Empty;
                    var cleanName = rawName.TrimStart('@');
                    if (string.IsNullOrWhiteSpace(cleanName))
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    writer.WriteString("Name", cleanName);

                    var typeRef = SnapshotWriterUtilities.BuildTypeRef(parameter);
                    if (!string.IsNullOrWhiteSpace(typeRef))
                    {
                        writer.WriteString("TypeRef", typeRef);
                        SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, typeRef);
                    }
                    else
                    {
                        var sqlTypeName = SnapshotWriterUtilities.BuildSqlTypeName(parameter);
                        if (!string.IsNullOrWhiteSpace(sqlTypeName))
                        {
                            writer.WriteString("SqlTypeName", sqlTypeName);
                        }
                    }

                    if (SnapshotWriterUtilities.ShouldEmitIsNullable(parameter.is_nullable == 1, typeRef))
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }

                    int? maxLength = null;
                    if (parameter.normalized_length > 0)
                    {
                        maxLength = parameter.normalized_length;
                    }
                    else if (parameter.max_length > 0)
                    {
                        maxLength = parameter.max_length;
                    }

                    if (parameter.max_length == -1)
                    {
                        maxLength = null;
                    }

                    if (maxLength.HasValue && SnapshotWriterUtilities.ShouldEmitMaxLength(maxLength.Value, typeRef))
                    {
                        writer.WriteNumber("MaxLength", maxLength.Value);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitPrecision(parameter.precision, typeRef))
                    {
                        writer.WriteNumber("Precision", parameter.precision);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitScale(parameter.scale, typeRef))
                    {
                        writer.WriteNumber("Scale", parameter.scale);
                    }

                    if (parameter.is_output == 1)
                    {
                        writer.WriteBoolean("IsOutput", true);
                    }

                    if (parameter.has_default_value == 1)
                    {
                        writer.WriteBoolean("HasDefaultValue", true);
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();

            if (isTableValued && columns != null && columns.Count > 0)
            {
                writer.WritePropertyName("Columns");
                writer.WriteStartArray();
                foreach (var column in columns)
                {
                    if (column == null)
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    if (!string.IsNullOrWhiteSpace(column.column_name))
                    {
                        writer.WriteString("Name", column.column_name);
                    }

                    var columnTypeRef = SnapshotWriterUtilities.BuildTypeRef(column);
                    if (!string.IsNullOrWhiteSpace(columnTypeRef))
                    {
                        writer.WriteString("TypeRef", columnTypeRef);
                        SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, columnTypeRef);
                    }
                    else
                    {
                        var sqlTypeName = SnapshotWriterUtilities.BuildSqlTypeName(column);
                        if (!string.IsNullOrWhiteSpace(sqlTypeName))
                        {
                            writer.WriteString("SqlTypeName", sqlTypeName);
                        }
                    }

                    if (SnapshotWriterUtilities.ShouldEmitIsNullable(column.is_nullable == 1, columnTypeRef))
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }

                    int? maxLength = null;
                    if (column.normalized_length > 0)
                    {
                        maxLength = column.normalized_length;
                    }
                    else if (column.max_length > 0)
                    {
                        maxLength = column.max_length;
                    }

                    if (column.max_length == -1)
                    {
                        maxLength = null;
                    }

                    if (maxLength.HasValue && SnapshotWriterUtilities.ShouldEmitMaxLength(maxLength.Value, columnTypeRef))
                    {
                        writer.WriteNumber("MaxLength", maxLength.Value);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitPrecision(column.precision, columnTypeRef))
                    {
                        writer.WriteNumber("Precision", column.precision);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitScale(column.scale, columnTypeRef))
                    {
                        writer.WriteNumber("Scale", column.scale);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] BuildTableJson(Table table, IReadOnlyList<Column> columns, ISet<string>? requiredTypeRefs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", table?.SchemaName ?? string.Empty);
            writer.WriteString("Name", table?.Name ?? string.Empty);
            WriteTableColumns(writer, columns, requiredTypeRefs);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] BuildTableCacheJson(Table table, IReadOnlyList<Column> columns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", table?.SchemaName ?? string.Empty);
            writer.WriteString("Name", table?.Name ?? string.Empty);

            if (table?.ObjectId > 0)
            {
                writer.WriteNumber("ObjectId", table.ObjectId);
            }

            if (table != null && table.ModifyDate != default)
            {
                var adjusted = table.ModifyDate.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(table.ModifyDate, DateTimeKind.Local)
                    : table.ModifyDate;
                writer.WriteString("ModifyDateUtc", adjusted.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            }

            WriteTableColumns(writer, columns, null);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteTableColumns(Utf8JsonWriter writer, IReadOnlyList<Column> columns, ISet<string>? requiredTypeRefs)
    {
        writer.WritePropertyName("Columns");
        writer.WriteStartArray();
        if (columns != null)
        {
            foreach (var column in columns)
            {
                if (column == null)
                {
                    continue;
                }

                writer.WriteStartObject();
                if (!string.IsNullOrWhiteSpace(column.Name))
                {
                    writer.WriteString("Name", column.Name);
                }

                var columnTypeRef = SnapshotWriterUtilities.BuildTypeRef(column);
                var effectiveTypeRef = columnTypeRef;
                if (string.IsNullOrWhiteSpace(effectiveTypeRef) && !string.IsNullOrWhiteSpace(column.SqlTypeName))
                {
                    var normalized = SnapshotWriterUtilities.NormalizeSqlTypeName(column.SqlTypeName);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        effectiveTypeRef = SnapshotWriterUtilities.BuildTypeRef("sys", normalized);
                    }
                }

                if (!string.IsNullOrWhiteSpace(effectiveTypeRef))
                {
                    writer.WriteString("TypeRef", effectiveTypeRef);
                    SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, effectiveTypeRef);
                }
                else if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
                {
                    writer.WriteString("SqlTypeName", column.SqlTypeName);
                }

                if (SnapshotWriterUtilities.ShouldEmitIsNullable(column.IsNullable, effectiveTypeRef ?? column.SqlTypeName))
                {
                    writer.WriteBoolean("IsNullable", true);
                }

                if (SnapshotWriterUtilities.ShouldEmitMaxLength(column.MaxLength, effectiveTypeRef))
                {
                    writer.WriteNumber("MaxLength", column.MaxLength);
                }

                var precision = column.Precision;
                if (SnapshotWriterUtilities.ShouldEmitPrecision(precision, effectiveTypeRef))
                {
                    writer.WriteNumber("Precision", precision.GetValueOrDefault());
                }

                var scale = column.Scale;
                if (SnapshotWriterUtilities.ShouldEmitScale(scale, effectiveTypeRef))
                {
                    writer.WriteNumber("Scale", scale.GetValueOrDefault());
                }

                if (column.IsIdentityRaw.HasValue && column.IsIdentityRaw.Value == 1)
                {
                    writer.WriteBoolean("IsIdentity", true);
                }

                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static byte[] BuildTableTypeJson(TableType tableType, IReadOnlyList<Column> columns, ISet<string>? requiredTypeRefs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", tableType?.SchemaName ?? string.Empty);
            writer.WriteString("Name", tableType?.Name ?? string.Empty);

            writer.WritePropertyName("Columns");
            writer.WriteStartArray();
            if (columns != null)
            {
                foreach (var column in columns)
                {
                    if (column == null)
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    if (!string.IsNullOrWhiteSpace(column.Name))
                    {
                        writer.WriteString("Name", column.Name);
                    }

                    var columnTypeRef = SnapshotWriterUtilities.BuildTypeRef(column);
                    var effectiveTypeRef = columnTypeRef;
                    if (string.IsNullOrWhiteSpace(effectiveTypeRef) && !string.IsNullOrWhiteSpace(column.SqlTypeName))
                    {
                        var normalized = SnapshotWriterUtilities.NormalizeSqlTypeName(column.SqlTypeName);
                        if (!string.IsNullOrWhiteSpace(normalized))
                        {
                            effectiveTypeRef = SnapshotWriterUtilities.BuildTypeRef("sys", normalized);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(effectiveTypeRef))
                    {
                        writer.WriteString("TypeRef", effectiveTypeRef);
                        SnapshotWriterUtilities.RegisterTypeRef(requiredTypeRefs, effectiveTypeRef);
                    }
                    else if (!string.IsNullOrWhiteSpace(column.SqlTypeName))
                    {
                        writer.WriteString("SqlTypeName", column.SqlTypeName);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitIsNullable(column.IsNullable, effectiveTypeRef ?? column.SqlTypeName))
                    {
                        writer.WriteBoolean("IsNullable", true);
                    }

                    if (SnapshotWriterUtilities.ShouldEmitMaxLength(column.MaxLength, effectiveTypeRef))
                    {
                        writer.WriteNumber("MaxLength", column.MaxLength);
                    }

                    var columnPrecision = column.Precision;
                    if (SnapshotWriterUtilities.ShouldEmitPrecision(columnPrecision, effectiveTypeRef))
                    {
                        writer.WriteNumber("Precision", columnPrecision.GetValueOrDefault());
                    }

                    var columnScale = column.Scale;
                    if (SnapshotWriterUtilities.ShouldEmitScale(columnScale, effectiveTypeRef))
                    {
                        writer.WriteNumber("Scale", columnScale.GetValueOrDefault());
                    }

                    if (column.IsIdentityRaw.HasValue && column.IsIdentityRaw.Value == 1)
                    {
                        writer.WriteBoolean("IsIdentity", true);
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static byte[] BuildScalarTypeJson(UserDefinedTypeRow type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", type?.schema_name ?? string.Empty);
            writer.WriteString("Name", type?.user_type_name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(type?.base_type_name))
            {
                writer.WriteString("BaseSqlTypeName", type.base_type_name);
            }

            if (type?.max_length > 0)
            {
                writer.WriteNumber("MaxLength", type.max_length);
            }

            if (type?.precision > 0)
            {
                writer.WriteNumber("Precision", type.precision);
            }

            if (type?.scale > 0)
            {
                writer.WriteNumber("Scale", type.scale);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void PruneExtraneousFiles(string directory, HashSet<string> validFileNames)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var existingFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var path in existingFiles)
            {
                var fileName = Path.GetFileName(path);
                if (validFileNames != null && validFileNames.Contains(fileName))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }
}
