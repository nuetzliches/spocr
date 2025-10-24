using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpocRVNext.Metadata; // reuse TableTypeInfo & ColumnInfo + TableTypeMetadataProvider
using SpocR.SpocRVNext.Metadata; // bring descriptor record types into scope
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.Metadata
{

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
        IReadOnlyList<FunctionDescriptor> GetFunctions();
    }

    public sealed class SchemaMetadataProvider : ISchemaMetadataProvider
    {
        private readonly string _projectRoot;
        private bool _loaded;
        private List<ProcedureDescriptor> _procedures = new();
        private List<InputDescriptor> _inputs = new();
        private List<OutputDescriptor> _outputs = new();
        private List<ResultSetDescriptor> _resultSets = new();
        private List<ResultDescriptor> _results = new();
    private List<FunctionDescriptor> _functions = new();

        public SchemaMetadataProvider(string? projectRoot = null)
        {
            _projectRoot = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : Path.GetFullPath(projectRoot!);
        }

        public IReadOnlyList<ProcedureDescriptor> GetProcedures() { EnsureLoaded(); return _procedures; }
        public IReadOnlyList<InputDescriptor> GetInputs() { EnsureLoaded(); return _inputs; }
        public IReadOnlyList<OutputDescriptor> GetOutputs() { EnsureLoaded(); return _outputs; }
        public IReadOnlyList<ResultSetDescriptor> GetResultSets() { EnsureLoaded(); return _resultSets; }
        public IReadOnlyList<ResultDescriptor> GetResults() { EnsureLoaded(); return _results; }
    public IReadOnlyList<FunctionDescriptor> GetFunctions() { EnsureLoaded(); return _functions; }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            Load();
            _loaded = true;
        }

        private void Load()
        {
            var schemaDir = Path.Combine(_projectRoot, ".spocr", "schema");
            if (!Directory.Exists(schemaDir)) { SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Info: schema directory not found: {schemaDir}"); return; }
            var indexPath = Path.Combine(schemaDir, "index.json");
            JsonDocument? doc = null;
            bool expanded = false;
            if (File.Exists(indexPath))
            {
                try
                {
                    using var fs = File.OpenRead(indexPath);
                    doc = JsonDocument.Parse(fs);
                    expanded = true;
                    SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Using expanded snapshot index: {indexPath}");
                }
                catch (Exception ex)
                {
                    SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Warning: failed to parse expanded index.json: {ex.Message}");
                    doc = null; // fallback legacy
                }
            }
            if (doc == null)
            {
                // Legacy fallback: pick latest non-index *.json (monolith)
                var files = Directory.GetFiles(schemaDir, "*.json")
                    .Where(f => !string.Equals(Path.GetFileName(f), "index.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (files.Length == 0) { SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Info: no legacy snapshot files in {schemaDir}"); return; }
                var ordered = files.Select(f => new FileInfo(f)).OrderByDescending(fi => fi.LastWriteTimeUtc).ToList();
                foreach (var fi in ordered.Take(5))
                    SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] legacy snapshot candidate: {fi.FullName} (utc={fi.LastWriteTimeUtc:O} size={fi.Length})");
                var latest = ordered.First();
                SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Using legacy snapshot: {latest.FullName}");
                try
                {
                    using var fs = File.OpenRead(latest.FullName);
                    doc = JsonDocument.Parse(fs);
                }
                catch (Exception ex)
                {
                    SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Warning: failed to parse legacy snapshot {latest.FullName}: {ex.Message}");
                    return;
                }
            }
            if (doc == null) return;
            JsonElement procsEl;
            if (expanded)
            {
                // Expanded index: wir müssen prozedur-dateien einzeln laden – index enthält nur File Hash Entries
                if (doc.RootElement.TryGetProperty("Procedures", out var procIndexEl) && procIndexEl.ValueKind == JsonValueKind.Array)
                {
                    var procEntries = procIndexEl.EnumerateArray().Select(e => new
                    {
                        File = e.GetPropertyOrDefault("File"),
                        Name = e.GetPropertyOrDefault("Name"),
                        Schema = e.GetPropertyOrDefault("Schema")
                    }).Where(x => !string.IsNullOrWhiteSpace(x.File)).ToList();
                    var procArray = new List<JsonElement>();
                    foreach (var entry in procEntries)
                    {
                        var path = Path.Combine(schemaDir, "procedures", entry.File!);
                        if (!File.Exists(path)) continue;
                        try
                        {
                            using var pfs = File.OpenRead(path);
                            using var pdoc = JsonDocument.Parse(pfs);
                            procArray.Add(pdoc.RootElement.Clone());
                        }
                        catch (Exception ex)
                        {
                            SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Warning: failed to parse procedure file {path}: {ex.Message}");
                        }
                    }
                    // Künstlich ein Array JsonElement bauen
                    using var tmpDoc = JsonDocument.Parse("[]"); // placeholder
                    // Wir können keinen neuen JsonElement dynamisch erzeugen ohne Utf8JsonWriter -> konvertieren über re-serialize
                    var procJson = System.Text.Json.JsonSerializer.Serialize(procArray);
                    doc = JsonDocument.Parse(procJson);
                    procsEl = doc.RootElement; // doc.root ist jetzt das Array der Procs
                }
                else
                {
                    SchemaMetadataProviderLogHelper.TryLog("[spocr vNext] Warning: expanded index.json missing Procedures array");
                    return;
                }
            }
            else
            {
                if (!doc.RootElement.TryGetProperty("Procedures", out procsEl) || procsEl.ValueKind != JsonValueKind.Array)
                {
                    if (!doc.RootElement.TryGetProperty("StoredProcedures", out procsEl) || procsEl.ValueKind != JsonValueKind.Array)
                    {
                        SchemaMetadataProviderLogHelper.TryLog("[spocr vNext] Warning: snapshot has no 'Procedures' or 'StoredProcedures' array");
                        return;
                    }
                    else
                    {
                        SchemaMetadataProviderLogHelper.TryLog("[spocr vNext] Info: using legacy 'StoredProcedures' key");
                    }
                }
            }

            var procList = new List<ProcedureDescriptor>();
            var inputList = new List<InputDescriptor>();
            var outputList = new List<OutputDescriptor>();
            var rsList = new List<ResultSetDescriptor>();
            var resultDescriptors = new List<ResultDescriptor>();
            var functionDescriptors = new List<FunctionDescriptor>();

            // Enricher vorbereiten (AST + Tabellen-Metadaten)
            var jsonTypeEnricher = new JsonResultSetTypeEnricher(_projectRoot);

            foreach (var p in procsEl.EnumerateArray())
            {
                var schema = p.GetPropertyOrDefault("Schema") ?? "dbo";
                var name = p.GetPropertyOrDefault("Name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var sanitized = NamePolicy.Sanitize(name);
                var operationName = $"{schema}.{sanitized}";
                string? rawSql = p.GetPropertyOrDefault("Sql")
                               ?? p.GetPropertyOrDefault("Definition")
                               ?? p.GetPropertyOrDefault("Body")
                               ?? p.GetPropertyOrDefault("Tsql");

                // Inputs & outputs (output params marked IsOutput or separate array)
                var inputParams = new List<FieldDescriptor>();
                var outputParams = new List<FieldDescriptor>();
                if (p.TryGetProperty("Inputs", out var inputsEl) && inputsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ip in inputsEl.EnumerateArray())
                    {
                        var raw = ip.GetPropertyOrDefault("Name") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var clean = raw.TrimStart('@');
                        var sqlType = ip.GetPropertyOrDefault("SqlTypeName") ?? string.Empty;
                        var maxLen = ip.GetPropertyOrDefaultInt("MaxLength");
                        var isNullable = SpocR.SpocRVNext.Metadata.SchemaMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(ip, "IsNullable");
                        var isOutput = ip.GetPropertyOrDefaultBool("IsOutput");
                        bool isTableType = ip.GetPropertyOrDefaultBool("IsTableType");
                        var ttSchema = ip.GetPropertyOrDefault("TableTypeSchema");
                        var ttName = ip.GetPropertyOrDefault("TableTypeName");
                        FieldDescriptor fd;
                        // Treat parameter as table type if either explicit flag set OR TableTypeName provided
                        if ((isTableType || !string.IsNullOrWhiteSpace(ttName)) && !string.IsNullOrWhiteSpace(ttName))
                        {
                            // Direkte UDTT-Zuordnung: CLR-Typ = Pascal(TableTypeName) (kein Renaming, nur Sonderzeichen bereinigen)
                            var pascal = NamePolicy.Sanitize(ttName!);
                            var attrs = new List<string> { "[TableType]" };
                            if (!string.IsNullOrWhiteSpace(ttSchema)) attrs.Add($"[TableTypeSchema({ttSchema})]");
                            fd = new FieldDescriptor(clean, NamePolicy.Sanitize(clean), pascal, false, sqlType, maxLen, Documentation: null, Attributes: attrs);
                        }
                        else
                        {
                            var clr = MapSqlToClr(sqlType, isNullable);
                            fd = new FieldDescriptor(clean, NamePolicy.Sanitize(clean), clr, isNullable, sqlType, maxLen);
                        }
                        if (isOutput) outputParams.Add(fd); else inputParams.Add(fd);
                    }
                }
                if (p.TryGetProperty("OutputParameters", out var outsEl) && outsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opEl in outsEl.EnumerateArray())
                    {
                        var raw = opEl.GetPropertyOrDefault("Name") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var clean = raw.TrimStart('@');
                        if (outputParams.Any(o => o.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))) continue;
                        var sqlType = opEl.GetPropertyOrDefault("SqlTypeName") ?? string.Empty;
                        var maxLen = opEl.GetPropertyOrDefaultInt("MaxLength");
                        var isNullable = SpocR.SpocRVNext.Metadata.SchemaMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(opEl, "IsNullable");
                        var clr = MapSqlToClr(sqlType, isNullable);
                        var fd = new FieldDescriptor(clean, NamePolicy.Sanitize(clean), clr, isNullable, sqlType, maxLen);
                        outputParams.Add(fd);
                    }
                }

                // Result sets with always-on resolver
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
                                var isNullable = SpocR.SpocRVNext.Metadata.SchemaMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(c, "IsNullable");
                                var clr = MapSqlToClr(sqlType, isNullable);
                                ColumnReferenceInfo? refInfo = null;
                                bool? deferred = null;
                                try
                                {
                                    if (c.TryGetProperty("Reference", out var refEl) && refEl.ValueKind == JsonValueKind.Object)
                                    {
                                        var kind = refEl.GetPropertyOrDefault("Kind");
                                        var schemaRef = refEl.GetPropertyOrDefault("Schema");
                                        var nameRef = refEl.GetPropertyOrDefault("Name");
                                        if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(nameRef))
                                        {
                                            refInfo = new ColumnReferenceInfo(kind!, schemaRef ?? "dbo", nameRef!);
                                        }
                                    }
                                    if (c.TryGetProperty("DeferredJsonExpansion", out var defEl))
                                    {
                                        if (defEl.ValueKind == JsonValueKind.True) deferred = true; else if (defEl.ValueKind == JsonValueKind.False) deferred = null; // nur true behalten
                                    }
                                }
                                catch { }
                                columns.Add(new FieldDescriptor(colName, NamePolicy.Sanitize(colName), clr, isNullable, sqlType, maxLen, Reference: refInfo, DeferredJsonExpansion: deferred));
                            }
                        }
                        var rsName = ResultSetNaming.DeriveName(idx, columns, usedNames);
                        if (!string.IsNullOrWhiteSpace(rawSql))
                        {
                            try
                            {
                                var suggested = ResultSetNameResolver.TryResolve(idx, rawSql!);
                                if (!string.IsNullOrWhiteSpace(suggested) && rsName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase))
                                {
                                    var baseNameUnique = NamePolicy.Sanitize(suggested!);
                                    var final = baseNameUnique;
                                    if (usedNames.Contains(final))
                                    {
                                        // Duplicate base table: append incremental numeric suffix (Users, Users1, Users2 ...)
                                        int suffix = 1;
                                        while (usedNames.Contains(final))
                                        {
                                            final = baseNameUnique + suffix.ToString();
                                            suffix++;
                                        }
                                    }
                                    rsName = final;
                                }
                            }
                            catch { /* silent fallback */ }
                        }
                        usedNames.Add(rsName);
                        // Forwarding + diagnostic metadata (optional in snapshot)
                        string? execSourceSchema = null;
                        string? execSourceProc = null;
                        bool hasSelectStar = false;
                        try
                        {
                            execSourceSchema = rse.GetPropertyOrDefault("ExecSourceSchemaName");
                            execSourceProc = rse.GetPropertyOrDefault("ExecSourceProcedureName");
                            var hasStarRaw = rse.GetPropertyOrDefaultBool("HasSelectStar");
                            hasSelectStar = hasStarRaw;
                        }
                        catch { /* best effort; leave null/defaults */ }
                        bool returnsJson = false;
                        bool returnsJsonArray = false;
                        try
                        {
                            returnsJson = rse.GetPropertyOrDefaultBool("ReturnsJson");
                            returnsJsonArray = rse.GetPropertyOrDefaultBool("ReturnsJsonArray");
                        }
                        catch { /* best effort */ }
                        ColumnReferenceInfo? rsRef = null;
                        if (!string.IsNullOrWhiteSpace(execSourceProc))
                        {
                            rsRef = new ColumnReferenceInfo("Procedure", execSourceSchema ?? "dbo", execSourceProc!);
                        }
                        resultSetDescriptors.Add(new ResultSetDescriptor(
                            Index: idx,
                            Name: rsName,
                            Fields: columns,
                            IsScalar: false,
                            Optional: true,
                            HasSelectStar: hasSelectStar,
                            ExecSourceSchemaName: execSourceSchema,
                            ExecSourceProcedureName: execSourceProc,
                            ReturnsJson: returnsJson,
                            ReturnsJsonArray: returnsJsonArray,
                            Reference: rsRef
                        ));
                        idx++;
                    }
                }

                // AST-basierte Typableitung für JSON ResultSets mit fehlenden SqlTypeName Werten
                try
                {
                    jsonTypeEnricher.Enrich(rawSql, resultSetDescriptors);
                }
                catch { /* defensiv: Enricher darf Ladeprozess nicht unterbrechen */ }

                var procDescriptor = new ProcedureDescriptor(
                    ProcedureName: name,
                    Schema: schema,
                    OperationName: operationName,
                    InputParameters: inputParams,
                    OutputFields: outputParams,
                    ResultSets: resultSetDescriptors
                );
                procList.Add(procDescriptor);
                if (resultSetDescriptors.Count > 0)
                {
                    var primary = resultSetDescriptors[0];
                    var payloadType = NamePolicy.Sanitize(operationName) + NamePolicy.Sanitize(primary.Name) + "Row";
                    resultDescriptors.Add(new ResultDescriptor(operationName, payloadType));
                }

                if (inputParams.Count > 0) inputList.Add(new InputDescriptor(operationName, inputParams));
                if (outputParams.Count > 0) outputList.Add(new OutputDescriptor(operationName, outputParams));
                if (resultSetDescriptors.Count > 0) rsList.AddRange(resultSetDescriptors);
            }

            _procedures = procList.OrderBy(p => p.Schema).ThenBy(p => p.ProcedureName).ToList();
            _inputs = inputList.OrderBy(i => i.OperationName).ToList();
            // UDTT Recovery Block entfernt – direkte Snapshot-Auswertung ersetzt Heuristik.
            _outputs = outputList.OrderBy(o => o.OperationName).ToList();
            _resultSets = rsList.OrderBy(r => r.Name).ToList();
            _results = resultDescriptors.OrderBy(r => r.OperationName).ToList();

            // Functions (expanded snapshot only)
            try
            {
                if (expanded)
                {
                    // Load functions directory entries from index.json? index.json holds FunctionsVersion + Function file hashes
                    var fnSchemaDir = Path.Combine(_projectRoot, ".spocr", "schema");
                    var fnIndexPath = Path.Combine(fnSchemaDir, "index.json");
                    if (File.Exists(fnIndexPath))
                    {
                        using var idxFs = File.OpenRead(fnIndexPath);
                        using var idxDoc = JsonDocument.Parse(idxFs);
                        if (idxDoc.RootElement.TryGetProperty("FunctionsVersion", out var fv) && fv.ValueKind != JsonValueKind.Null)
                        {
                            // Enumerate function file entries
                            if (idxDoc.RootElement.TryGetProperty("Functions", out var fnsEl) && fnsEl.ValueKind == JsonValueKind.Array)
                            {
                                var fnEntries = fnsEl.EnumerateArray().Select(e => new
                                {
                                    File = e.GetPropertyOrDefault("File"),
                                    Name = e.GetPropertyOrDefault("Name"),
                                    Schema = e.GetPropertyOrDefault("Schema")
                                }).Where(x => !string.IsNullOrWhiteSpace(x.File)).ToList();
                                foreach (var entry in fnEntries)
                                {
                                    var path = Path.Combine(fnSchemaDir, "functions", entry.File!);
                                    if (!File.Exists(path)) continue;
                                    try
                                    {
                                        using var ffs = File.OpenRead(path);
                                        using var fdoc = JsonDocument.Parse(ffs);
                                        var root = fdoc.RootElement;
                                        var schema = root.GetPropertyOrDefault("Schema") ?? entry.Schema ?? "dbo";
                                        var name = root.GetPropertyOrDefault("Name") ?? entry.Name ?? string.Empty;
                                        if (string.IsNullOrWhiteSpace(name)) continue;
                                        bool isTableValued = root.GetPropertyOrDefaultBool("IsTableValued");
                                        var returnSql = root.GetPropertyOrDefault("ReturnSqlType");
                                        var returnMaxLenVal = root.GetPropertyOrDefaultInt("ReturnMaxLength");
                                        int? returnMaxLen = returnMaxLenVal > 0 ? returnMaxLenVal : null;
                                        bool? returnIsNullable = null;
                                        if (root.TryGetProperty("ReturnIsNullable", out var rin) && rin.ValueKind != JsonValueKind.Null)
                                        {
                                            if (rin.ValueKind == JsonValueKind.True) returnIsNullable = true; else if (rin.ValueKind == JsonValueKind.False) returnIsNullable = false; // speichern nur falls vorhanden
                                        }
                                        bool returnsJson = root.GetPropertyOrDefaultBool("ReturnsJson");
                                        bool returnsJsonArray = root.GetPropertyOrDefaultBool("ReturnsJsonArray");
                                        var jsonRoot = root.GetPropertyOrDefault("JsonRootProperty");
                                        bool encrypted = root.GetPropertyOrDefaultBool("IsEncrypted");
                                        // Dependencies
                                        var dependencies = new List<string>();
                                        if (root.TryGetProperty("Dependencies", out var depsEl) && depsEl.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var de in depsEl.EnumerateArray())
                                            {
                                                if (de.ValueKind == JsonValueKind.String)
                                                {
                                                    var dep = de.GetString();
                                                    if (!string.IsNullOrWhiteSpace(dep)) dependencies.Add(dep!);
                                                }
                                            }
                                        }
                                        // Parameters
                                        var paramDescriptors = new List<FunctionParameterDescriptor>();
                                        if (root.TryGetProperty("Parameters", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var pe in paramsEl.EnumerateArray())
                                            {
                                                var raw = pe.GetPropertyOrDefault("Name") ?? string.Empty;
                                                if (string.IsNullOrWhiteSpace(raw)) continue;
                                                var clean = raw.TrimStart('@');
                                                var sqlType = pe.GetPropertyOrDefault("SqlTypeName") ?? pe.GetPropertyOrDefault("SqlType") ?? string.Empty;
                                                var maxLenVal = pe.GetPropertyOrDefaultInt("MaxLength");
                                                int maxLen = maxLenVal ?? 0;
                                                bool isNullable = SpocR.SpocRVNext.Metadata.SchemaMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(pe, "IsNullable");
                                                bool isOutput = pe.GetPropertyOrDefaultBool("IsOutput");
                                                var clr = SqlClrTypeMapper.Map(sqlType, isNullable);
                                                paramDescriptors.Add(new FunctionParameterDescriptor(clean, sqlType, clr, isNullable, maxLen <= 0 ? null : maxLen, isOutput));
                                            }
                                        }
                                        // Columns (TVF)
                                        var colDescriptors = new List<TableValuedFunctionColumnDescriptor>();
                                        if (isTableValued && root.TryGetProperty("Columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (var ce in colsEl.EnumerateArray())
                                            {
                                                var colName = ce.GetPropertyOrDefault("Name") ?? string.Empty;
                                                if (string.IsNullOrWhiteSpace(colName)) continue;
                                                var sqlType = ce.GetPropertyOrDefault("SqlTypeName") ?? ce.GetPropertyOrDefault("SqlType") ?? string.Empty;
                                                bool isNullable = SpocR.SpocRVNext.Metadata.SchemaMetadataProviderJsonExtensions.GetPropertyOrDefaultBoolStrict(ce, "IsNullable");
                                                var maxLenVal = ce.GetPropertyOrDefaultInt("MaxLength");
                                                int maxLen = maxLenVal ?? 0;
                                                var clr = SqlClrTypeMapper.Map(sqlType, isNullable);
                                                colDescriptors.Add(new TableValuedFunctionColumnDescriptor(colName, sqlType, clr, isNullable, maxLen <= 0 ? null : maxLen));
                                            }
                                        }
                                        functionDescriptors.Add(new FunctionDescriptor(
                                            SchemaName: schema,
                                            FunctionName: name,
                                            IsTableValued: isTableValued,
                                            ReturnSqlType: string.IsNullOrWhiteSpace(returnSql) ? null : returnSql,
                                            ReturnMaxLength: returnMaxLen,
                                            ReturnIsNullable: returnIsNullable,
                                            ReturnsJson: returnsJson,
                                            ReturnsJsonArray: returnsJsonArray,
                                            JsonRootProperty: string.IsNullOrWhiteSpace(jsonRoot) ? null : jsonRoot,
                                            IsEncrypted: encrypted,
                                            Dependencies: dependencies,
                                            Parameters: paramDescriptors,
                                            Columns: colDescriptors
                                        ));
                                    }
                                    catch (Exception fx) { SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Warning: failed to parse function file {path}: {fx.Message}"); }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SchemaMetadataProviderLogHelper.TryLog($"[spocr vNext] Warning: function descriptors load failed: {ex.Message}");
            }
            _functions = functionDescriptors.OrderBy(f => f.SchemaName).ThenBy(f => f.FunctionName).ToList();
            if (_procedures.Count == 0)
            {
                SchemaMetadataProviderLogHelper.TryLog("[spocr vNext] Warning: 0 procedures parsed from snapshot (expanded/legacy)");
            }
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

    internal static class SchemaMetadataProviderJsonExtensions
    {
        public static bool GetPropertyOrDefaultBoolStrict(this JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return false; // fehlend => false (NOT NULL default)
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
                return false;
            }
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt32(out var i)) return i != 0;
                return false;
            }
            return false;
        }
    }
}

namespace SpocR.SpocRVNext.Metadata
{
    internal static class SchemaMetadataProviderLogHelper
    {
        public static void TryLog(string message)
        {
            try { Console.Out.WriteLine(message); } catch { }
        }
    }
}
