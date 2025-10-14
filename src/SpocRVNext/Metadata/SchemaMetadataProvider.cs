using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpocR.SpocRVNext.Metadata;
using SpocRVNext.Metadata; // reuse TableTypeInfo & ColumnInfo
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Aggregates metadata from the latest snapshot under .spocr/schema for procedures, their inputs, outputs, and result sets.
/// Provides strongly typed descriptor lists used by vNext generators.
/// </summary>
public interface ISchemaMetadataProvider
{
    IReadOnlyList<ProcedureDescriptor> GetProcedures();
    IReadOnlyList<InputDescriptor> GetInputs();
    IReadOnlyList<OutputDescriptor> GetOutputs();
    IReadOnlyList<ResultSetDescriptor> GetResultSets();
    IReadOnlyList<ResultDescriptor> GetResults();
}

internal sealed class SchemaMetadataProvider : ISchemaMetadataProvider
{
    private readonly string _projectRoot;
    private bool _loaded;
    private List<ProcedureDescriptor> _procedures = new();
    private List<InputDescriptor> _inputs = new();
    private List<OutputDescriptor> _outputs = new();
    private List<ResultSetDescriptor> _resultSets = new();
    private List<ResultDescriptor> _results = new();

    public SchemaMetadataProvider(string? projectRoot = null)
    {
        _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : Path.GetFullPath(projectRoot!);
    }

    public IReadOnlyList<ProcedureDescriptor> GetProcedures() { EnsureLoaded(); return _procedures; }
    public IReadOnlyList<InputDescriptor> GetInputs() { EnsureLoaded(); return _inputs; }
    public IReadOnlyList<OutputDescriptor> GetOutputs() { EnsureLoaded(); return _outputs; }
    public IReadOnlyList<ResultSetDescriptor> GetResultSets() { EnsureLoaded(); return _resultSets; }
    public IReadOnlyList<ResultDescriptor> GetResults() { EnsureLoaded(); return _results; }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        Load();
        _loaded = true;
    }

    private void Load()
    {
        var schemaDir = Path.Combine(_projectRoot, ".spocr", "schema");
        if (!Directory.Exists(schemaDir)) return;
        var files = Directory.GetFiles(schemaDir, "*.json");
        if (files.Length == 0) return;
        var latest = files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc).First();
        try { Console.Out.WriteLine($"[spocr vNext] Using schema snapshot: {latest.FullName}"); } catch { }
        using var fs = File.OpenRead(latest.FullName);
        using var doc = JsonDocument.Parse(fs);
        if (!doc.RootElement.TryGetProperty("Procedures", out var procsEl) || procsEl.ValueKind != JsonValueKind.Array) return;

        var procList = new List<ProcedureDescriptor>();
        var inputList = new List<InputDescriptor>();
        var outputList = new List<OutputDescriptor>();
        var rsList = new List<ResultSetDescriptor>();
        var resultDescriptors = new List<ResultDescriptor>();

        foreach (var p in procsEl.EnumerateArray())
        {
            var schema = p.GetPropertyOrDefault("Schema") ?? "dbo";
            var name = p.GetPropertyOrDefault("Name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var sanitized = NamePolicy.Sanitize(name);
            // Include schema prefix so downstream generators can split schema reliably
            var operationName = $"{schema}.{sanitized}";
            // Optional: Roh-SQL falls im Snapshot vorhanden (noch nicht standardisiert). Unterstützte Keys: Sql, Definition, Body, Tsql
            string? rawSql = p.GetPropertyOrDefault("Sql")
                           ?? p.GetPropertyOrDefault("Definition")
                           ?? p.GetPropertyOrDefault("Body")
                           ?? p.GetPropertyOrDefault("Tsql");

            // Inputs (includes potential output parameters flagged IsOutput true)
            var inputParams = new List<FieldDescriptor>();
            var outputParams = new List<FieldDescriptor>();
            if (p.TryGetProperty("Inputs", out var inputsEl) && inputsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ip in inputsEl.EnumerateArray())
                {
                    var raw = ip.GetPropertyOrDefault("Name") ?? string.Empty; // e.g. @UserId
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clean = raw.TrimStart('@');
                    var sqlType = ip.GetPropertyOrDefault("SqlTypeName") ?? string.Empty;
                    var maxLen = ip.GetPropertyOrDefaultInt("MaxLength");
                    var isNullable = ip.GetPropertyOrDefaultBool("IsNullable");
                    var clr = MapSqlToClr(sqlType, isNullable);
                    var fd = new FieldDescriptor(clean, NamePolicy.Sanitize(clean), clr, isNullable, sqlType, maxLen);
                    var isOutput = ip.GetPropertyOrDefaultBool("IsOutput");
                    if (isOutput) outputParams.Add(fd); else inputParams.Add(fd);
                }
            }

            // Support alternative snapshot shape: separate "OutputParameters" array (each treated as output parameter)
            if (p.TryGetProperty("OutputParameters", out var outsEl) && outsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var opEl in outsEl.EnumerateArray())
                {
                    var raw = opEl.GetPropertyOrDefault("Name") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var clean = raw.TrimStart('@');
                    // Avoid duplicates if already captured via Inputs/IsOutput
                    if (outputParams.Any(o => o.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) continue;
                    var sqlType = opEl.GetPropertyOrDefault("SqlTypeName") ?? string.Empty;
                    var maxLen = opEl.GetPropertyOrDefaultInt("MaxLength");
                    var isNullable = opEl.GetPropertyOrDefaultBool("IsNullable");
                    var clr = MapSqlToClr(sqlType, isNullable);
                    var fd = new FieldDescriptor(clean, NamePolicy.Sanitize(clean), clr, isNullable, sqlType, maxLen);
                    outputParams.Add(fd);
                }
            }

            // Result sets
            var resultSetDescriptors = new List<ResultSetDescriptor>();
            if (p.TryGetProperty("ResultSets", out var rsEl) && rsEl.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rse in rsEl.EnumerateArray())
                {
                    var columns = new List<FieldDescriptor>();
                    if (rse.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in colsEl.EnumerateArray())
                        {
                            var colName = c.GetPropertyOrDefault("Name") ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(colName)) continue;
                            var sqlType = c.GetPropertyOrDefault("SqlTypeName") ?? string.Empty;
                            var maxLen = c.GetPropertyOrDefaultInt("MaxLength");
                            var isNullable = c.GetPropertyOrDefaultBool("IsNullable");
                            var clr = MapSqlToClr(sqlType, isNullable);
                            columns.Add(new FieldDescriptor(colName, NamePolicy.Sanitize(colName), clr, isNullable, sqlType, maxLen));
                        }
                    }
                    var rsName = ResultSetNaming.DeriveName(idx, columns, usedNames);
                    // Versuch: Falls Roh-SQL vorhanden, heuristischen Namen ableiten (nicht breaking; nur wenn eindeutig sinnvoll)
                    if (!string.IsNullOrWhiteSpace(rawSql))
                    {
                        try
                        {
                            var suggested = ResultSetNameResolver.TryResolve(idx, rawSql!);
                            if (!string.IsNullOrWhiteSpace(suggested))
                            {
                                // Nur ersetzen, wenn der generische Name 'ResultSetX' ist und Suggest nicht kollidiert
                                if (rsName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase) && !usedNames.Contains(suggested))
                                {
                                    rsName = NamePolicy.Sanitize(suggested!);
                                    usedNames.Add(rsName); // Sicherstellen, dass nicht erneut vergeben
                                }
                            }
                        }
                        catch { /* Silent fallback */ }
                    }
                    usedNames.Add(rsName);
                    resultSetDescriptors.Add(new ResultSetDescriptor(idx, rsName, columns));
                    idx++;
                }
            }

            var procDescriptor = new ProcedureDescriptor(
                ProcedureName: name,
                Schema: schema,
                OperationName: operationName,
                InputParameters: inputParams,
                OutputFields: outputParams,
                ResultSets: resultSetDescriptors
            );
            procList.Add(procDescriptor);

            // ResultDescriptor strategy: choose first result set (if any) as primary payload type name.
            if (resultSetDescriptors.Count > 0)
            {
                var primary = resultSetDescriptors[0];
                // Build a synthetic payload type name (Operation + primary set name + "Row") for future model (even if not yet generated)
                var payloadType = NamePolicy.Sanitize(operationName) + NamePolicy.Sanitize(primary.Name) + "Row";
                resultDescriptors.Add(new ResultDescriptor(operationName, payloadType));
            }

            if (inputParams.Count > 0)
            {
                inputList.Add(new InputDescriptor(operationName, inputParams));
            }
            if (outputParams.Count > 0)
            {
                outputList.Add(new OutputDescriptor(operationName, outputParams));
            }
            if (resultSetDescriptors.Count > 0)
            {
                foreach (var rs in resultSetDescriptors)
                {
                    rsList.Add(rs);
                }
            }
        }

        _procedures = procList.OrderBy(p => p.Schema).ThenBy(p => p.ProcedureName).ToList();
        _inputs = inputList.OrderBy(i => i.OperationName).ToList();
        _outputs = outputList.OrderBy(o => o.OperationName).ToList();
        _resultSets = rsList.OrderBy(r => r.Name).ToList();
        _results = resultDescriptors.OrderBy(r => r.OperationName).ToList();
    }

    private static string MapSqlToClr(string sql, bool nullable)
    {
        sql = sql.ToLowerInvariant();
        string core = sql switch
        {
            var s when s.StartsWith("int") => "int",
            var s when s.StartsWith("bigint") => "long",
            var s when s.StartsWith("smallint") => "short",
            var s when s.StartsWith("tinyint") => "byte",
            var s when s.StartsWith("bit") => "bool",
            var s when s.StartsWith("decimal") || s.StartsWith("numeric") => "decimal",
            var s when s.StartsWith("float") => "double",
            var s when s.StartsWith("real") => "float",
            var s when s.Contains("date") || s.Contains("time") => "DateTime",
            var s when s.Contains("uniqueidentifier") => "Guid",
            var s when s.Contains("binary") || s.Contains("varbinary") => "byte[]",
            var s when s.Contains("char") || s.Contains("text") => "string",
            _ => "string"
        };
        if (core != "string" && core != "byte[]" && nullable) core += "?";
        return core;
    }
}
