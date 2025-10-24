using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using SpocR.SpocRVNext.Engine;
using SpocR.SpocRVNext.Metadata;
using SpocRVNext.Configuration;
using SpocR.SpocRVNext.Utils;
using SpocR.Models;
using System.Text.Json;
using SpocR.SpocRVNext.Diagnostics;

namespace SpocR.SpocRVNext.Generators;

public sealed class ProceduresGenerator
{
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<ProcedureDescriptor>> _provider;
    private readonly EnvConfiguration? _cfg;
    private readonly string _projectRoot;

    public ProceduresGenerator(ITemplateRenderer renderer, Func<IReadOnlyList<ProcedureDescriptor>> provider, ITemplateLoader? loader = null, string? projectRoot = null, EnvConfiguration? cfg = null)
    {
        _renderer = renderer;
        _provider = provider;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
        _cfg = cfg; // may be null for legacy call sites
    }

    public int Generate(string ns, string baseOutputDir)
    {
        // Capture full unfiltered list (needed for cross-schema forwarding even if target schema excluded by allow-list)
        var allProcedures = _provider();
        var originalLookup = allProcedures
            .GroupBy(p => (p.Schema ?? "dbo") + "." + (p.ProcedureName ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Work list subject to filters
        var procs = allProcedures.ToList();
        
        // Check for explicit procedure filter first
        var buildProceduresRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_PROCEDURES");
        var hasExplicitProcedures = !string.IsNullOrWhiteSpace(buildProceduresRaw);
        
        // 1) Positive allow-list: prefer EnvConfiguration.BuildSchemas; fallback to direct env var only if cfg missing
        // Skip schema filtering if procedures are explicitly specified
        HashSet<string>? buildSchemas = null;
        if (!hasExplicitProcedures)
        {
            if (_cfg?.BuildSchemas is { Count: > 0 })
            {
                buildSchemas = new HashSet<string>(_cfg.BuildSchemas, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var buildSchemasRaw = Environment.GetEnvironmentVariable("SPOCR_BUILD_SCHEMAS");
                if (!string.IsNullOrWhiteSpace(buildSchemasRaw))
                {
                    buildSchemas = new HashSet<string>(buildSchemasRaw!
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
                }
            }
            if (buildSchemas is { Count: > 0 })
            {
                var before = procs.Count;
                // Filter strictly by schema list
                procs = procs.Where(p => buildSchemas.Contains(p.Schema ?? "dbo")).ToList();
                var removed = before - procs.Count;
                try { Console.Out.WriteLine($"[spocr vNext] Info: BuildSchemas allow-list active -> {procs.Count} of {before} procedures retained. Removed: {removed}. Schemas: {string.Join(",", buildSchemas)}"); } catch { }
            }
        }

        // 1.5) Procedure-specific allow-list (SPOCR_BUILD_PROCEDURES) 
        // DISABLED in Code Generation - procedure filtering is only for schema snapshots (pull command)
        // Code generation is controlled exclusively by SPOCR_BUILD_SCHEMAS
        /*
        HashSet<string>? buildProcedures = null;
        if (!string.IsNullOrWhiteSpace(buildProceduresRaw))
        {
            buildProcedures = new HashSet<string>(buildProceduresRaw!
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
        }
        if (buildProcedures is { Count: > 0 })
        {
            var before = procs.Count;
            // Filter by procedure list (format: schema.procedurename or just procedurename)
            procs = procs.Where(p =>
            {
                var fullName = $"{p.Schema}.{p.ProcedureName}";
                var procedureName = p.ProcedureName;
                return buildProcedures.Contains(fullName) || buildProcedures.Contains(procedureName);
            }).ToList();
            var removed = before - procs.Count;
            try { Console.Out.WriteLine($"[spocr vNext] Info: BuildProcedures allow-list active -> {procs.Count} of {before} procedures retained. Removed: {removed}. Procedures: {string.Join(",", buildProcedures)}"); } catch { }
        }
        */

        // 2) Dynamic negative filter only when no positive list is active
        //    Reads ignored schemas from spocr.json (if present) and/or ENV override
        List<string> ignoredSchemas = new();
        try
        {
            var configPath = Path.Combine(_projectRoot, "spocr.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                ConfigurationModel? cfg = null;
                try { cfg = JsonSerializer.Deserialize<ConfigurationModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { /* ignore parse errors */ }
                if (cfg?.Project?.IgnoredSchemas != null)
                {
                    ignoredSchemas.AddRange(cfg.Project.IgnoredSchemas.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                else if (cfg?.Schema?.Any() == true)
                {
                    ignoredSchemas.AddRange(cfg.Schema.Where(s => s.Status == SchemaStatusEnum.Ignore).Select(s => s.Name));
                }
            }
            // ENV Override (Komma-separiert): SPOCR_IGNORED_SCHEMAS
            var envIgnored = Environment.GetEnvironmentVariable("SPOCR_IGNORED_SCHEMAS");
            if (!string.IsNullOrWhiteSpace(envIgnored))
            {
                ignoredSchemas.AddRange(envIgnored.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            }
        }
        catch { /* silent */ }
        if ((buildSchemas == null || buildSchemas.Count == 0) && ignoredSchemas.Count > 0)
        {
            var ignoredSet = new HashSet<string>(ignoredSchemas, StringComparer.OrdinalIgnoreCase);
            var before = procs.Count;
            procs = procs.Where(p => !ignoredSet.Contains(p.Schema ?? "dbo")).ToList();
            var removed = before - procs.Count;
            if (removed > 0)
            {
                try { Console.Out.WriteLine($"[spocr vNext] Info: Filtered ignored schemas (dynamic): removed {removed} procedures (total now {procs.Count})."); } catch { }
            }
        }
        if (procs.Count == 0)
        {
            try { Console.Out.WriteLine("[spocr vNext] Info: ProceduresGenerator skipped – provider returned 0 procedures."); } catch { }
            return 0;
        }
        // Entfernte Forwarding-Klonphase: ExecSource Platzhalter bleiben unverändert.
        // Expansion erfolgt später beim Template-Mapping (generationszeitlich), nicht durch Mutation der ResultSets.
        bool IsDebug()
        {
            var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL");
            return lvl != null && (lvl.Equals("debug", StringComparison.OrdinalIgnoreCase) || lvl.Equals("trace", StringComparison.OrdinalIgnoreCase));
        }
        try
        {
            if (IsDebug())
            {
                foreach (var fp in procs)
                {
                    Console.Out.WriteLine($"[proc-forward-debug-summary] {fp.Schema}.{fp.ProcedureName} sets={fp.ResultSets.Count} -> {string.Join(";", fp.ResultSets.Select(r => r.Name + ":" + r.Fields.Count))}");
                }
            }
        }
        catch { }
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl)) header = headerTpl.TrimEnd() + Environment.NewLine;
        // Emit ExecutionSupport once (if template present and file missing or stale)
        if (_loader != null && _loader.TryLoad("ExecutionSupport", out var execTpl))
        {
            var execPath = Path.Combine(baseOutputDir, "ExecutionSupport.cs");
            bool write = !File.Exists(execPath);
            if (!write)
            {
                try
                {
                    var existing = File.ReadAllText(execPath);
                    // Rewrite wenn Namespace falsch ODER TvpHelper fehlt ODER ReaderUtil fehlt (Template aktualisiert)
                    if (!existing.Contains($"namespace {ns};") || !existing.Contains("TvpHelper") || !existing.Contains("ReaderUtil")) write = true;
                }
                catch { write = true; }
            }
            if (write)
            {
                var execModel = new { Namespace = ns, HEADER = header };
                var code = _renderer.Render(execTpl, execModel);
                File.WriteAllText(execPath, code);
            }
        }
        // StoredProcedure template no longer used after consolidation
        var written = 0;
        string? unifiedTemplateRaw = null;
        bool hasUnifiedTemplate = _loader != null && _loader.TryLoad("UnifiedProcedure", out unifiedTemplateRaw);
        if (!hasUnifiedTemplate)
        {
            try
            {
                Console.Out.WriteLine("[spocr vNext] Warn: UnifiedProcedure.spt not found – generating fallback skeleton (check template path)");
                if (_loader != null)
                {
                    var names = string.Join(",", _loader.ListNames());
                    Console.Out.WriteLine("[spocr vNext] Template loader names: " + (names.Length == 0 ? "<empty>" : names));
                }
            }
            catch { }
        }
        foreach (var proc in procs.OrderBy(p => p.OperationName))
        {
            var op = proc.OperationName;
            string schemaPart = proc.Schema ?? "dbo";
            string procPart = op;
            var idx = op.IndexOf('.');
            if (idx > 0)
            {
                schemaPart = op.Substring(0, idx);
                procPart = op[(idx + 1)..];
            }
            var schemaPascal = ToPascalCase(schemaPart);
            var finalNs = ns + "." + schemaPascal;
            var schemaDir = Path.Combine(baseOutputDir, schemaPascal);
            Directory.CreateDirectory(schemaDir);
            var procedureTypeName = NamePolicy.Procedure(procPart);
            // Aggregat-Typ: rein <Proc>Aggregate (ohne zusätzliches 'Result')
            // Align with existing tests expecting <Proc>Result as unified aggregate type
            var unifiedResultTypeName = NamePolicy.Result(procPart);
            var inputTypeName = NamePolicy.Input(procPart);
            var outputTypeName = NamePolicy.Output(procPart);
            // JSON Typkorrektur-Tracking für diese Prozedur (außerhalb des Template-Blocks, damit nachher verfügbar)
            var jsonTypeCorrections = new List<string>();

            // Cross-Schema EXEC Forwarding stub:
            // ProcedureDescriptor currently lacks raw SQL text; forwarding requires SQL to detect EXEC-only wrappers.
            // No-op for now; real implementation will enrich metadata (SqlText) and then merge target result sets.
            // Placeholder log (verbose) can be enabled later via env flag SPOCR_FORWARDING_DIAG.
            // if (Environment.GetEnvironmentVariable("SPOCR_FORWARDING_DIAG") == "1")
            //     try { Console.Out.WriteLine($"[proc-forward-xschema][skip] Missing SqlText metadata for {proc.Schema}.{proc.ProcedureName}"); } catch { }

            // Cleanup alte Dateien (<Proc>Result.cs, <Proc>Input.cs, <Proc>Output.cs) bevor neue Einzeldatei erstellt wird
            try
            {
                var legacyFiles = new[]
                {
                    Path.Combine(schemaDir, procPart + "Result.cs"),
                    Path.Combine(schemaDir, procPart + "Input.cs"),
                    Path.Combine(schemaDir, procPart + "Output.cs")
                };
                foreach (var lf in legacyFiles)
                {
                    if (File.Exists(lf)) File.Delete(lf);
                }
            }
            catch { }
            string finalCode;
            if (hasUnifiedTemplate && unifiedTemplateRaw != null)
            {
                // Dynamische Usings inkl. Cross-Schema TableType Referenzen
                var usingSet = new HashSet<string>
                {
                    "using System;",
                    "using System.Collections.Generic;",
                    "using System.Linq;",
                    "using System.Data;",
                    "using System.Data.Common;",
                    "using System.Threading;",
                    "using System.Threading.Tasks;",
                    $"using {ns};"
                };
                // Füge zusätzliche Schema-Namespace Usings hinzu falls Input-Parameter TableTypes anderer Schemas referenzieren
                foreach (var ipParam in proc.InputParameters)
                {
                    if (ipParam.Attributes != null && ipParam.Attributes.Any(a => a.StartsWith("[TableTypeSchema(", StringComparison.Ordinal)))
                    {
                        var attr = ipParam.Attributes.First(a => a.StartsWith("[TableTypeSchema(", StringComparison.Ordinal));
                        var schemaNameRaw = attr.Substring("[TableTypeSchema(".Length);
                        schemaNameRaw = schemaNameRaw.TrimEnd(')', ' ');
                        if (!string.IsNullOrWhiteSpace(schemaNameRaw))
                        {
                            var schemaPascalX = ToPascalCase(schemaNameRaw);
                            // Skip if same schema as current proc
                            if (!schemaPascalX.Equals(schemaPascal, StringComparison.Ordinal))
                            {
                                usingSet.Add($"using {ns}.{schemaPascalX};");
                            }
                        }
                    }
                }
                var usingBlock = string.Join("\n", usingSet.OrderBy(u => u));

                // Structured metadata for template-driven record generation
                var rsMeta = new List<object>();
                int rsIdx = 0;
                // jsonTypeCorrections bereits oben initialisiert
                // Generation-time Expansion: Wenn ein ResultSet ein reiner ExecSource Platzhalter (Fields leer) ist,
                // werden dessen Ziel-ResultSets virtuell expandiert (inline), ohne die ursprüngliche Descriptor-Liste zu verändern.
                foreach (var rs in proc.ResultSets.OrderBy(r => r.Index))
                {
                    bool isExecPlaceholder = rs.Fields.Count == 0 && ((rs.ExecSourceProcedureName != null) || (rs.Reference?.Kind == "Procedure"));
                    if (isExecPlaceholder)
                    {
                        var targetSchema = rs.Reference?.Schema ?? rs.ExecSourceSchemaName ?? "dbo";
                        var targetProcName = rs.Reference?.Name ?? rs.ExecSourceProcedureName;
                        var targetKey = targetSchema + "." + targetProcName;
                        if (originalLookup.TryGetValue(targetKey, out var targetProc) && targetProc.ResultSets != null && targetProc.ResultSets.Count > 0)
                        {
                            if (IsDebug()) { try { Console.Out.WriteLine($"[proc-forward-expand] {proc.Schema}.{proc.ProcedureName} expanding placeholder -> {targetKey} sets={targetProc.ResultSets.Count}"); } catch { } }
                            foreach (var tSet in targetProc.ResultSets)
                            {
                                // Skip empty target ResultSets in virtual expansion
                                if (tSet.Fields.Count == 0)
                                {
                                    if (IsDebug()) { try { Console.Out.WriteLine($"[proc-virtual-skip] {proc.Schema}.{proc.ProcedureName} skipping empty target ResultSet {tSet.Name} from {targetKey}"); } catch { } }
                                    continue;
                                }

                                // Virtuelle Projektion: benutze Ziel-Felder & JSON Flags, setze ExecSource* auf Platzhalter Herkunft
                                var virtualRs = new ResultSetDescriptor(
                                    Index: rsIdx,
                                    Name: tSet.Name,
                                    Fields: tSet.Fields,
                                    IsScalar: tSet.IsScalar,
                                    Optional: tSet.Optional,
                                    HasSelectStar: tSet.HasSelectStar,
                                    ExecSourceSchemaName: rs.ExecSourceSchemaName,
                                    ExecSourceProcedureName: rs.ExecSourceProcedureName,
                                    ReturnsJson: tSet.ReturnsJson,
                                    ReturnsJsonArray: tSet.ReturnsJsonArray,
                                    Reference: rs.Reference ?? (targetProcName != null ? new ColumnReferenceInfo("Procedure", targetSchema, targetProcName) : null)
                                );
                                // Weiterverarbeitung wie reguläres ResultSet (Mapping & Record-Emission)
                                AppendResultSetMeta(virtualRs);
                            }
                            continue; // Platzhalter selbst nicht zusätzlich emittieren
                        }
                        else
                        {
                            if (IsDebug()) { try { Console.Out.WriteLine($"[proc-forward-expand][skip] target missing for {targetKey}"); } catch { } }
                            // Platzhalter mit leerem Ziel wird übersprungen - keine leeren Records generieren
                            continue;
                        }
                    }
                    // Nur ResultSets mit Feldern verarbeiten (leere ResultSets aller Art überspringen)
                    if (rs.Fields.Count == 0)
                    {
                        if (IsDebug()) { try { Console.Out.WriteLine($"[proc-skip-empty] {proc.Schema}.{proc.ProcedureName} skipping empty ResultSet {rs.Name}"); } catch { } }
                        continue;
                    }
                    AppendResultSetMeta(rs);
                }

                void AppendResultSetMeta(ResultSetDescriptor rs)
                {
                    // JSON Typ-Korrektur: Wenn ReturnsJson aktiv ist, Feldliste mit SQL->CLR Mapping neu ableiten.
                    IReadOnlyList<FieldDescriptor> effectiveFields = rs.Fields;
                    if (rs.ReturnsJson && rs.Fields.Count > 0)
                    {
                        var remapped = new List<FieldDescriptor>(rs.Fields.Count);
                        foreach (var f in rs.Fields)
                        {
                            var mapped = MapJsonSqlToClr(f.SqlTypeName, f.IsNullable);
                            if (!string.Equals(mapped, f.ClrType, StringComparison.Ordinal))
                            {
                                remapped.Add(new FieldDescriptor(f.Name, f.PropertyName, mapped, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes));
                                jsonTypeCorrections.Add($"{proc.OperationName}:{rs.Name}.{f.PropertyName} {f.ClrType}->{mapped}");
                            }
                            else
                            {
                                remapped.Add(f);
                            }
                        }
                        effectiveFields = remapped;
                    }
                    // Generator-Phase: Deferred JSON Funktions-Expansion (RecordAsJson etc.)
                    // Ersetzt Container-Spalte durch virtuelle Dot-Pfad Felder: record.<col>
                    try
                    {
                        var deferredContainers = effectiveFields.Where(f => f.DeferredJsonExpansion == true && f.Reference?.Kind == "Function" && !string.IsNullOrWhiteSpace(f.Reference.Name)).ToList();
                        if (deferredContainers.Count > 0 && StoredProcedureContentModel.ResolveFunctionJsonSet != null)
                        {
                            var expanded = new List<FieldDescriptor>(effectiveFields);
                            bool changed = false;
                            foreach (var dc in deferredContainers)
                            {
                                var refInfo = dc.Reference!; // bereits gefiltert auf non-null
                                (bool ReturnsJson, bool ReturnsJsonArray, string RootProperty, IReadOnlyList<string> ColumnNames) meta;
                                try { meta = StoredProcedureContentModel.ResolveFunctionJsonSet(refInfo.Schema ?? "dbo", refInfo.Name); } catch { continue; }
                                if (!meta.ReturnsJson || meta.ColumnNames == null || meta.ColumnNames.Count == 0) continue;
                                // Entferne Container-Spalte
                                expanded.Remove(dc);
                                foreach (var colName in meta.ColumnNames)
                                {
                                    if (string.IsNullOrWhiteSpace(colName)) continue;
                                    var pathName = dc.Name + "." + colName.Trim();
                                    // Leaf als string (Typ-Anreicherung über separaten Enricher zukünftige Erweiterung)
                                    expanded.Add(new FieldDescriptor(pathName, pathName, "string", true, "nvarchar", null, null, null));
                                }
                                changed = true;
                            }
                            if (changed)
                            {
                                // Stabilisiere Reihenfolge für deterministische Ausgabe
                                effectiveFields = expanded.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
                            }
                        }
                    }
                    catch { }
                    // Typ-Namensschema an Tests angleichen:
                    // Verwende konsistent NamePolicy.ResultSet(procPart, rs.Name) für jeden ResultSet Record.
                    // Unified Aggregate bleibt NamePolicy.Result(procPart) und kollidiert somit nicht mehr mit erstem Satz.
                    string rsType = NamePolicy.ResultSet(procPart, rs.Name);
                    // Suffix-Korrektur entfällt im neuen Schema
                    // Alias-basierte Property-Namen zuerst bestimmen (für JSON-Fallback erforderlich)
                    var usedNames = new HashSet<string>(StringComparer.Ordinal);
                    var aliasProps = new List<string>();
                    foreach (var f in effectiveFields)
                    {
                        var candidate = AliasToIdentifier(f.Name);
                        if (!usedNames.Add(candidate))
                        {
                            var suffix = 1;
                            while (!usedNames.Add(candidate + suffix.ToString())) suffix++;
                            candidate = candidate + suffix.ToString();
                        }
                        aliasProps.Add(candidate);
                    }
                    var ordinalAssignments = effectiveFields.Select((f, idx) => $"int o{idx}=ReaderUtil.TryGetOrdinal(r, \"{f.Name}\");").ToList();
                    // Nur noch optionaler First-Row Dump (Spalten-Missing- & Column-Dump Instrumentierung entfernt)
                    // Removed debug artifact (SPOCR_DUMP_FIRST_ROW) – first row dump instrumentation eliminated for cleaner generated output.
                    // JSON Aggregator Fallback (FOR JSON PATH) falls alle Ordinals < 0 und trotzdem Feld-Metadata vorhanden
                    var allMissingCondition = rs.Fields.Count > 0 ? string.Join(" && ", rs.Fields.Select((f, i) => $"o{i} < 0")) : "false"; // nur für Nicht-JSON Sets relevant
                    // ReturnsJson Flag aus Snapshot nutzen (ResultSet Modell besitzt Properties)
                    // JSON-Erkennung: jedes ResultSet mit ReturnsJson wird über Single-Column Deserialisierung verarbeitet (FOR JSON liefert genau eine NVARCHAR-Spalte).
                    var isJson = rs.ReturnsJson;
                    var isJsonArray = rs.ReturnsJsonArray;
                    string jsonFallback = string.Empty;
                    if (isJson)
                    {
                        // Vereinfachte direkte Deserialisierung: SQL liefert genau eine Zeile mit einer NVARCHAR(MAX) JSON-Spalte.
                        // Keine Schleife, kein Fallback. Flags steuern Array vs Single.
                        var optionsLiteral = "JsonSupport.Options";
                        if (isJsonArray)
                        {
                            jsonFallback = $"{{ if (await r.ReadAsync(ct).ConfigureAwait(false) && !r.IsDBNull(0)) {{ var __raw = r.GetString(0); try {{ var __list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<{rsType}>>(__raw, {optionsLiteral}); if (__list != null) foreach (var __e in __list) list.Add(__e); }} catch {{ }} }} }}";
                        }
                        else
                        {
                            jsonFallback = $"{{ if (await r.ReadAsync(ct).ConfigureAwait(false) && !r.IsDBNull(0)) {{ var __raw = r.GetString(0); try {{ var __single = System.Text.Json.JsonSerializer.Deserialize<{rsType}>(__raw, {optionsLiteral}); if (__single != null) list.Add(__single); }} catch {{ }} }} }}";
                        }
                    }
                    string ordinalDecls;
                    if (isJson)
                    {
                        // JSON ResultSet: keine Ordinal-Ermittlung, nur Deserialisierungscode
                        ordinalDecls = jsonFallback;
                    }
                    else
                    {
                        ordinalDecls = string.Join(" ", ordinalAssignments); // classic mapping with cached ordinals (debug dump removed)
                    }
                    var fieldExprs = string.Join(", ", effectiveFields.Select((f, idx) => MaterializeFieldExpressionCached(f, idx)));
                    // Property Namen bleiben analog: Result, Result1, Result2 ... (keine Änderung nötig für Tests)
                    string propName = rsIdx == 0 ? "Result" : "Result" + rsIdx.ToString();
                    var initializerExpr = $"rs.Length > {rsIdx} && rs[{rsIdx}] is object[] rows{rsIdx} ? Array.ConvertAll(rows{rsIdx}, o => ({rsType})o).ToList() : (rs.Length > {rsIdx} && rs[{rsIdx}] is System.Collections.Generic.List<object> list{rsIdx} ? Array.ConvertAll(list{rsIdx}.ToArray(), o => ({rsType})o).ToList() : Array.Empty<{rsType}>())";
                    // BodyBlock ersetzt Template-If-Verwendung; enthält vollständigen Lambda-Inhalt.
                    string bodyBlock;
                    if (isJson)
                    {
                        bodyBlock = $"var list = new System.Collections.Generic.List<object>(); {jsonFallback} return list;";
                    }
                    else
                    {
                        var whileLoop = $"while (await r.ReadAsync(ct).ConfigureAwait(false)) {{ list.Add(new {rsType}({fieldExprs})); }}";
                        bodyBlock = $"var list = new System.Collections.Generic.List<object>(); {ordinalDecls} {whileLoop} return list;";
                    }
                    // Nested JSON sub-struct generation (JSON Sets): nur '.' trennt Hierarchie; Unterstriche bleiben literal
                    string nestedRecordsBlock = string.Empty;
                    if (isJson && effectiveFields.Any(f => f.Name.Contains('.')))
                    {
                        // Build hierarchical tree
                        var rootLeafFields = new List<FieldDescriptor>();
                        var groupOrder = new List<string>();
                        var groups = new Dictionary<string, List<FieldDescriptor>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in effectiveFields)
                        {
                            if (!f.Name.Contains('.'))
                            {
                                // Kein '.' => bleibt Leaf (Unterstriche werden NICHT geparst)
                                rootLeafFields.Add(f);
                                continue;
                            }
                            var parts = f.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length <= 1)
                            {
                                rootLeafFields.Add(f);
                                continue;
                            }
                            var key = parts[0];
                            if (!groups.ContainsKey(key))
                            {
                                groups[key] = new List<FieldDescriptor>();
                                groupOrder.Add(key);
                            }
                            groups[key].Add(f);
                        }

                        string Pascal(string raw)
                        {
                            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
                            var segs = raw.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                            var b = new System.Text.StringBuilder();
                            foreach (var seg in segs)
                            {
                                var clean = new string(seg.Where(char.IsLetterOrDigit).ToArray());
                                if (clean.Length == 0) continue;
                                b.Append(char.ToUpperInvariant(clean[0]) + (clean.Length > 1 ? clean.Substring(1) : string.Empty));
                            }
                            var res = b.ToString();
                            if (res.Length == 0) res = "Segment";
                            if (char.IsDigit(res[0])) res = "N" + res;
                            return res;
                        }

                        string BuildNestedTypeName(string root, string segment) => (root.EndsWith("Result", StringComparison.Ordinal) ? root[..^"Result".Length] : root) + Pascal(segment) + "Result";

                        // Recursively build nested types for groups (supports deeper levels e.g. sourceAccount_type_code)
                        var builtTypes = new List<(string TypeName, string Code)>();

                        List<(string TypeName, string Code)> BuildGroup(string rootTypeName, string groupName, List<FieldDescriptor> fields)
                        {
                            // Partition fields into direct leaves (parts length ==2) and deeper
                            var leaves = new List<FieldDescriptor>();
                            var subGroups = new Dictionary<string, List<FieldDescriptor>>(StringComparer.OrdinalIgnoreCase);
                            foreach (var f in fields)
                            {
                                if (!f.Name.Contains('.'))
                                {
                                    // Keine weitere Hierarchie (Unterstriche bleiben erhalten)
                                    leaves.Add(new FieldDescriptor(f.Name, f.Name, f.ClrType, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes));
                                    continue;
                                }
                                var parts = f.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length == 2)
                                {
                                    leaves.Add(new FieldDescriptor(parts[1], parts[1], f.ClrType, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes));
                                }
                                else if (parts.Length > 2)
                                {
                                    var sub = parts[1];
                                    var remainder = string.Join('.', parts.Skip(1));
                                    var f2 = new FieldDescriptor(remainder, remainder, f.ClrType, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes);
                                    if (!subGroups.ContainsKey(sub)) subGroups[sub] = new List<FieldDescriptor>();
                                    subGroups[sub].Add(f2);
                                }
                            }
                            var typeName = BuildNestedTypeName(rootTypeName, groupName);
                            var paramLines = new List<string>();
                            // Kommas nur setzen, wenn entweder weitere Leaf-Parameter folgen oder Subgroups existieren
                            paramLines.AddRange(leaves.Select((lf, idx) =>
                            {
                                bool isLastLeaf = idx == leaves.Count - 1;
                                var needsComma = !isLastLeaf || subGroups.Count > 0;
                                return $"    {lf.ClrType} {lf.PropertyName}{(needsComma ? "," : string.Empty)}";
                            }));
                            // Add sub-group properties (types) preserving first appearance order
                            int sgIndex = 0;
                            foreach (var sg in subGroups)
                            {
                                var nestedList = BuildGroup(typeName, sg.Key, sg.Value); // recursion
                                builtTypes.AddRange(nestedList);
                                var nestedTypeName = BuildNestedTypeName(typeName, sg.Key);
                                var line = $"    {nestedTypeName} {sg.Key}{(sgIndex == subGroups.Count - 1 ? string.Empty : ",")}";
                                paramLines.Add(line);
                                sgIndex++;
                            }
                            var code = $"public readonly record struct {typeName}(\n" + string.Join("\n", paramLines) + "\n);\n";
                            return new List<(string TypeName, string Code)> { (typeName, code) };
                        }

                        foreach (var g in groupOrder)
                        {
                            var nestedList = BuildGroup(rsType, g, groups[g]);
                            builtTypes.AddRange(nestedList);
                        }
                        nestedRecordsBlock = string.Join("\n", builtTypes.Select(t => t.Code));

                        // Rebuild fieldsBlock for root: root leaves + group properties (alias-treu; keine PascalCase Umwandlung; '_' bleibt Bestandteil des Alias)
                        var rootParams = new List<string>();
                        for (int i = 0; i < rootLeafFields.Count; i++)
                        {
                            var lf = rootLeafFields[i];
                            var aliasName = lf.Name; // original alias
                            if (aliasName.Contains('.')) aliasName = aliasName.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
                            var comma = (i == rootLeafFields.Count - 1 && groupOrder.Count == 0) ? string.Empty : ",";
                            rootParams.Add($"    {lf.ClrType} {aliasName}{comma}");
                        }
                        // Group properties (escaped via AliasToIdentifier to protect reserved keywords like 'params')
                        for (int i = 0; i < groupOrder.Count; i++)
                        {
                            var g = groupOrder[i];
                            var gEsc = AliasToIdentifier(g);
                            var nestedTypeName = BuildNestedTypeName(rsType, g);
                            var comma = i == groupOrder.Count - 1 ? string.Empty : ",";
                            rootParams.Add($"    {nestedTypeName} {gEsc}{comma}");
                        }
                        var rootFieldsBlock = string.Join(Environment.NewLine, rootParams);
                        rsMeta.Add(new
                        {
                            Name = rs.Name,
                            TypeName = rsType,
                            PropName = propName,
                            OrdinalDecls = ordinalDecls,
                            FieldExprs = fieldExprs,
                            Index = rsIdx,
                            AggregateAssignment = initializerExpr,
                            FieldsBlock = rootFieldsBlock,
                            ReturnsJson = isJson,
                            ReturnsJsonArray = isJsonArray,
                            BodyBlock = bodyBlock,
                            NestedRecordsBlock = nestedRecordsBlock
                        });
                        rsIdx++;
                        return; // skip flat record path
                    }
                    // NEW: Verschachtelte Record-Generierung auch für nicht-JSON Sets mit Dot-Aliasen
                    // (Bisher nur JSON Sets erhielten nestedRecordsBlock – nun allgemeiner Ansatz).
                    if (!isJson && effectiveFields.Any(f => f.Name.Contains('.')))
                    {
                        // Build tree auf Basis von '.' Segmenten (Unterstriche bleiben wörtlich erhalten)
                        var rootLeafFields = new List<FieldDescriptor>();
                        var groupOrder = new List<string>();
                        var groups = new Dictionary<string, List<FieldDescriptor>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in effectiveFields)
                        {
                            if (!f.Name.Contains('.')) { rootLeafFields.Add(f); continue; }
                            var parts = f.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                            var key = parts[0];
                            if (!groups.ContainsKey(key)) { groups[key] = new List<FieldDescriptor>(); groupOrder.Add(key); }
                            // Rekonstruiere Rest ohne ersten Teil als zusammengesetzter Name (mit '.') zur späteren Auflösung
                            var remainder = string.Join('.', parts.Skip(1));
                            groups[key].Add(new FieldDescriptor(remainder, remainder, f.ClrType, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes));
                        }
                        string Pascal(string raw)
                        {
                            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
                            var segs = raw.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                            var b = new System.Text.StringBuilder();
                            foreach (var seg in segs)
                            {
                                var clean = new string(seg.Where(char.IsLetterOrDigit).ToArray());
                                if (clean.Length == 0) continue;
                                b.Append(char.ToUpperInvariant(clean[0]) + (clean.Length > 1 ? clean.Substring(1) : string.Empty));
                            }
                            var res = b.ToString();
                            if (res.Length == 0) res = "Segment";
                            if (char.IsDigit(res[0])) res = "N" + res;
                            return res;
                        }
                        string BuildNestedTypeName(string root, string segment) => (root.EndsWith("Result", StringComparison.Ordinal) ? root[..^"Result".Length] : root) + Pascal(segment) + "Result";

                        var builtTypes = new List<(string TypeName, string Code)>();
                        var exprLookup = effectiveFields.Select((f, idx) => (f, idx)).ToDictionary(t => t.f.Name, t => MaterializeFieldExpressionCached(t.f, t.idx), StringComparer.OrdinalIgnoreCase);

                        List<(string TypeName, string Code)> BuildGroup(string rootTypeName, string groupName, List<FieldDescriptor> fields, out string ctorExpr)
                        {
                            var leaves = new List<FieldDescriptor>();
                            var subGroups = new Dictionary<string, List<FieldDescriptor>>(StringComparer.OrdinalIgnoreCase);
                            foreach (var f in fields)
                            {
                                if (!f.Name.Contains('.')) { leaves.Add(f); continue; }
                                var parts = f.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                                var sub = parts[0];
                                var remainder = string.Join('.', parts.Skip(1));
                                if (!subGroups.ContainsKey(sub)) subGroups[sub] = new List<FieldDescriptor>();
                                subGroups[sub].Add(new FieldDescriptor(remainder, remainder, f.ClrType, f.IsNullable, f.SqlTypeName, f.MaxLength, f.Documentation, f.Attributes));
                            }
                            var typeNameNested = BuildNestedTypeName(rootTypeName, groupName);
                            var paramLines = new List<string>();
                            // Parameter Lines für nested type
                            for (int i = 0; i < leaves.Count; i++)
                            {
                                var lf = leaves[i];
                                var comma = (i == leaves.Count - 1 && subGroups.Count == 0) ? string.Empty : ",";
                                // Leaf PropertyName letzter Segment-Teil
                                var pName = lf.Name.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
                                paramLines.Add($"    {lf.ClrType} {pName}{comma}");
                            }
                            // Subgroups rekursiv
                            var subgroupCtorExprs = new List<string>();
                            int sgIdx = 0;
                            foreach (var sg in subGroups)
                            {
                                var nestedList = BuildGroup(typeNameNested, sg.Key, sg.Value, out var subCtor);
                                builtTypes.AddRange(nestedList);
                                var nestedTypeName = BuildNestedTypeName(typeNameNested, sg.Key);
                                var comma = sgIdx == subGroups.Count - 1 ? string.Empty : ",";
                                paramLines.Add($"    {nestedTypeName} {sg.Key}{comma}");
                                subgroupCtorExprs.Add(subCtor);
                                sgIdx++;
                            }
                            var code = $"public readonly record struct {typeNameNested}(\n" + string.Join("\n", paramLines) + "\n);\n";
                            // Constructor expression für diesen Gruppenknoten: new Type(a,b, new Child(...))
                            var leafExprs = leaves.Select(l => exprLookup.TryGetValue(ComposeFullName(groupName, l.Name), out var ex) ? ex : "default");
                            var totalArgs = leafExprs.Concat(subgroupCtorExprs);
                            ctorExpr = $"new {typeNameNested}(" + string.Join(", ", totalArgs) + ")";
                            return new List<(string TypeName, string Code)> { (typeNameNested, code) };
                        }
                        string ComposeFullName(string root, string remainder) => string.IsNullOrEmpty(remainder) ? root : (remainder.Contains('.') ? root + "." + remainder : root + "." + remainder);

                        var topGroupCtorExprs = new List<string>();
                        foreach (var g in groupOrder)
                        {
                            var nestedList = BuildGroup(rsType, g, groups[g], out var gCtor);
                            builtTypes.AddRange(nestedList);
                            topGroupCtorExprs.Add(gCtor);
                        }
                        // Root FieldsBlock: leaf root columns + group properties
                        var rootParams = new List<string>();
                        for (int i = 0; i < rootLeafFields.Count; i++)
                        {
                            var lf = rootLeafFields[i];
                            var comma = (i == rootLeafFields.Count - 1 && groupOrder.Count == 0) ? string.Empty : ",";
                            rootParams.Add($"    {lf.ClrType} {lf.Name}{comma}");
                        }
                        for (int i = 0; i < groupOrder.Count; i++)
                        {
                            var g = groupOrder[i];
                            var gEsc = AliasToIdentifier(g);
                            var nestedTypeName = BuildNestedTypeName(rsType, g);
                            var comma = i == groupOrder.Count - 1 ? string.Empty : ",";
                            rootParams.Add($"    {nestedTypeName} {gEsc}{comma}");
                        }
                        var rootFieldsBlock = string.Join(Environment.NewLine, rootParams);
                        // Mapping Argumente: root leaves gefolgt von group ctor exprs (konsistent zur Parameterreihenfolge)
                        var rootLeafExprs = rootLeafFields.Select(f => exprLookup.TryGetValue(f.Name, out var ex) ? ex : "default");
                        var constructorArgs = string.Join(", ", rootLeafExprs.Concat(topGroupCtorExprs));
                        // Passe BodyBlock (Zeilenlese-Variante) an: new rsType(constructorArgs)
                        if (!isJson)
                        {
                            var ordinalDeclNested = ordinalDecls; // identisch nutzen
                            var whileLoopNested = $"while (await r.ReadAsync(ct).ConfigureAwait(false)) {{ list.Add(new {rsType}({constructorArgs})); }}";
                            bodyBlock = $"var list = new System.Collections.Generic.List<object>(); {ordinalDeclNested} {whileLoopNested} return list;";
                        }
                        nestedRecordsBlock = string.Join("\n", builtTypes.Select(t => t.Code));
                        rsMeta.Add(new
                        {
                            Name = rs.Name,
                            TypeName = rsType,
                            PropName = propName,
                            OrdinalDecls = ordinalDecls,
                            FieldExprs = constructorArgs,
                            Index = rsIdx,
                            AggregateAssignment = initializerExpr,
                            FieldsBlock = rootFieldsBlock,
                            ReturnsJson = isJson,
                            ReturnsJsonArray = isJsonArray,
                            BodyBlock = bodyBlock,
                            NestedRecordsBlock = nestedRecordsBlock
                        });
                        rsIdx++;
                    }
                    else
                    {
                        var fieldsBlock = string.Join(Environment.NewLine, effectiveFields.Select((f, i) => $"    {f.ClrType} {aliasProps[i]}{(i == effectiveFields.Count - 1 ? string.Empty : ",")}"));
                        rsMeta.Add(new
                        {
                            Name = rs.Name,
                            TypeName = rsType,
                            PropName = propName,
                            OrdinalDecls = ordinalDecls,
                            FieldExprs = fieldExprs,
                            Index = rsIdx,
                            AggregateAssignment = initializerExpr,
                            FieldsBlock = fieldsBlock,
                            ReturnsJson = isJson,
                            ReturnsJsonArray = isJsonArray,
                            BodyBlock = bodyBlock,
                            NestedRecordsBlock = nestedRecordsBlock
                        });
                        rsIdx++;
                    }
                }

                // Parameters meta
                var paramLines = new List<string>();
                foreach (var ip in proc.InputParameters)
                {
                    var isTableType = ip.Attributes != null && ip.Attributes.Any(a => a.StartsWith("[TableType]", StringComparison.Ordinal));
                    if (isTableType)
                    {
                        // Use Object DbType placeholder; binder will override with SqlDbType.Structured
                        paramLines.Add($"new(\"@{ip.Name}\", System.Data.DbType.Object, null, false, false)");
                    }
                    else
                    {
                        paramLines.Add($"new(\"@{ip.Name}\", {MapDbType(ip.SqlTypeName)}, {EmitSize(ip)}, false, {ip.IsNullable.ToString().ToLowerInvariant()})");
                    }
                }
                foreach (var opf in proc.OutputFields)
                    paramLines.Add($"new(\"@{opf.Name}\", {MapDbType(opf.SqlTypeName)}, {EmitSize(opf)}, true, {opf.IsNullable.ToString().ToLowerInvariant()})");

                // Output factory args
                string outputFactoryArgs = proc.OutputFields.Count > 0 ? string.Join(", ", proc.OutputFields.Select(f => CastOutputValue(f))) : string.Empty;

                // Aggregate assignments
                var model = new
                {
                    Namespace = finalNs,
                    UsingDirectives = usingBlock,
                    HEADER = header,
                    HasParameters = proc.InputParameters.Count + proc.OutputFields.Count > 0,
                    HasInput = proc.InputParameters.Count > 0,
                    HasOutput = proc.OutputFields.Count > 0,
                    // Verwende rsMeta Count (inkl. virtueller Expansion) statt ursprünglicher proc.ResultSets
                    HasResultSets = rsMeta.Count > 0,
                    HasMultipleResultSets = rsMeta.Count > 1,
                    InputParameters = proc.InputParameters.Select((p, i) => new { p.ClrType, p.PropertyName, Comma = i == proc.InputParameters.Count - 1 ? string.Empty : "," }).ToList(),
                    OutputFields = proc.OutputFields.Select((f, i) => new { f.ClrType, f.PropertyName, Comma = i == proc.OutputFields.Count - 1 ? string.Empty : "," }).ToList(),
                    // Pre-bracket (and escape) schema & procedure name so runtime does not need to normalize.
                    ProcedureFullName = "[" + (proc.Schema ?? string.Empty).Replace("]", "]]", StringComparison.Ordinal) + "].[" + (proc.ProcedureName ?? string.Empty).Replace("]", "]]", StringComparison.Ordinal) + "]",
                    ProcedureTypeName = procedureTypeName,
                    UnifiedResultTypeName = unifiedResultTypeName,
                    OutputTypeName = outputTypeName,
                    PlanTypeName = procedureTypeName + "Plan",
                    InputTypeName = inputTypeName,
                    ParameterLines = paramLines,
                    InputAssignments = proc.InputParameters.Select(ip =>
                    {
                        var isTableType = ip.Attributes != null && ip.Attributes.Any(a => a.StartsWith("[TableType]", StringComparison.Ordinal));
                        if (isTableType)
                        {
                            // Build SqlDataRecord collection via reflection helper (ExecutionSupport.TvpHelper)
                            return $"{{ var prm = cmd.Parameters[\"@{ip.Name}\"]; prm.Value = TvpHelper.BuildRecords(input.{ip.PropertyName}) ?? (object)DBNull.Value; if (prm is Microsoft.Data.SqlClient.SqlParameter sp) sp.SqlDbType = System.Data.SqlDbType.Structured; }}";
                        }
                        return $"cmd.Parameters[\"@{ip.Name}\"].Value = input.{ip.PropertyName};";
                    }).ToList(),
                    ResultSets = rsMeta,
                    OutputFactoryArgs = outputFactoryArgs,
                    HasAggregateOutput = proc.OutputFields.Count > 0,
                    // AggregateAssignments removed; template uses ResultSets[].AggregateAssignment directly
                };
                finalCode = _renderer.Render(unifiedTemplateRaw!, model);
            }
            else
            {
                // Fallback: original inline build
                var fileSb = new StringBuilder();
                fileSb.Append(header);
                fileSb.AppendLine($"namespace {finalNs};");
                fileSb.AppendLine();
                fileSb.AppendLine("using System;\nusing System.Collections.Generic;\nusing System.Data;\nusing System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing " + ns + ";");
                // (For brevity, we could replicate blocks, but template should normally exist now)
                finalCode = fileSb.ToString();
            }
            finalCode = NormalizeWhitespace(finalCode);
            File.WriteAllText(Path.Combine(schemaDir, procPart + ".cs"), finalCode);
            written++;

            // Aggregierte Warnung für JSON Typkorrekturen (max. einmal pro Prozedur ausgeben, gekürzt)
            if (jsonTypeCorrections.Count > 0)
            {
                try
                {
                    // Log-Level gesteuert: Nur ausgeben bei SPOCR_LOG_LEVEL=debug|trace oder wenn Anzahl über Schwellwert (>=4)
                    var lvl = Environment.GetEnvironmentVariable("SPOCR_LOG_LEVEL");
                    bool verbose = lvl != null && (lvl.Equals("debug", StringComparison.OrdinalIgnoreCase) || lvl.Equals("trace", StringComparison.OrdinalIgnoreCase));
                    bool threshold = jsonTypeCorrections.Count >= 4;
                    if (verbose || threshold)
                    {
                        var sample = string.Join(", ", jsonTypeCorrections.Take(5));
                        Console.Out.WriteLine($"[spocr vNext] JsonTypeMapping: {jsonTypeCorrections.Count} field(s) corrected for {proc.OperationName}. Examples: {sample}{(jsonTypeCorrections.Count > 5 ? ", ..." : string.Empty)}");
                    }
                }
                catch { /* ignore */ }
            }
        }
        // JSON Audit Hook: optional report generation if env var set (SPOCR_JSON_AUDIT=1)
        try
        {
            if (Environment.GetEnvironmentVariable("SPOCR_JSON_AUDIT") == "1")
            {
                JsonResultSetAudit.WriteReport(_projectRoot, procs);
                Console.Out.WriteLine($"[spocr vNext] JsonAudit written: {Path.Combine(_projectRoot, "debug", "json-audit.txt")}");
            }
        }
        catch { /* ignore audit failures */ }
        try { Console.Out.WriteLine($"[spocr vNext] Generators succeeded (procedures written={written})"); } catch { }
        return written;
    }

    // Mapping speziell für JSON ResultSets (ähnlich MapSqlToClr, aber isoliert damit spätere Erweiterungen möglich sind)
    private static string MapJsonSqlToClr(string? sqlTypeName, bool nullable)
    {
        if (string.IsNullOrWhiteSpace(sqlTypeName)) return nullable ? "string?" : "string";
        var t = sqlTypeName.ToLowerInvariant();
        // Entferne Länge/Precision Angaben (z.B. decimal(18,2))
        var parenIdx = t.IndexOf('(');
        if (parenIdx >= 0) t = t.Substring(0, parenIdx);
        string core = t switch
        {
            "int" => "int",
            "bigint" => "long",
            "smallint" => "short",
            "tinyint" => "byte",
            "bit" => "bool",
            "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
            "float" => "double",
            "real" => "float",
            "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
            "datetimeoffset" => "DateTimeOffset",
            "time" => "TimeSpan",
            "uniqueidentifier" => "Guid",
            "varbinary" or "binary" or "image" or "rowversion" or "timestamp" => "byte[]",
            // JSON Properties können trotzdem nvarchar sein → string
            _ => "string"
        };
        if (core != "string" && core != "byte[]" && nullable) core += "?";
        // Für byte[] Nullable nicht unterscheiden (leeres Array statt null) – Designentscheidung
        return core;
    }

    private static string MapDbType(string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return "System.Data.DbType.String";
        var t = sqlType.ToLowerInvariant();
        // normalize common parentheses like nvarchar(50)
        if (t.Contains('(')) t = t[..t.IndexOf('(')];
        return t switch
        {
            "int" => "System.Data.DbType.Int32",
            "bigint" => "System.Data.DbType.Int64",
            "smallint" => "System.Data.DbType.Int16",
            "tinyint" => "System.Data.DbType.Byte",
            "bit" => "System.Data.DbType.Boolean",
            "decimal" or "numeric" or "money" or "smallmoney" => "System.Data.DbType.Decimal",
            "float" => "System.Data.DbType.Double",
            "real" => "System.Data.DbType.Single",
            "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => "System.Data.DbType.DateTime2",
            "uniqueidentifier" => "System.Data.DbType.Guid",
            "varbinary" or "binary" or "image" => "System.Data.DbType.Binary",
            "xml" => "System.Data.DbType.Xml",
            // treat all character & text types as string
            "varchar" or "nvarchar" or "char" or "nchar" or "text" or "ntext" => "System.Data.DbType.String",
            _ => "System.Data.DbType.String"
        };
    }

    private static string EmitSize(FieldDescriptor f)
        => f.MaxLength.HasValue && f.MaxLength.Value > 0 ? f.MaxLength.Value.ToString() : "null";

    private static string MaterializeFieldExpression(FieldDescriptor f)
    {
        var accessor = f.ClrType switch
        {
            "int" or "int?" => "GetInt32",
            "long" or "long?" => "GetInt64",
            "short" or "short?" => "GetInt16",
            "byte" or "byte?" => "GetByte",
            "bool" or "bool?" => "GetBoolean",
            "decimal" or "decimal?" => "GetDecimal",
            "double" or "double?" => "GetDouble",
            "float" or "float?" => "GetFloat",
            "DateTime" or "DateTime?" => "GetDateTime",
            "Guid" or "Guid?" => "GetGuid",
            _ => null
        };
        var prop = f.PropertyName;
        if (accessor == null)
        {
            // string, byte[], fallback
            if (f.ClrType.StartsWith("byte[]"))
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? System.Array.Empty<byte>() : (byte[])r[\"{f.Name}\"]";
            if (f.ClrType == "string")
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? string.Empty : r.GetString(r.GetOrdinal(\"{f.Name}\"))";
            if (f.ClrType == "string?")
                return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? null : r.GetString(r.GetOrdinal(\"{f.Name}\"))";
            return $"r[\"{f.Name}\"]"; // generic
        }
        var nullable = f.IsNullable && !f.ClrType.EndsWith("?") ? true : f.ClrType.EndsWith("?");
        if (nullable)
            return $"r.IsDBNull(r.GetOrdinal(\"{f.Name}\")) ? null : ({f.ClrType})r.{accessor}(r.GetOrdinal(\"{f.Name}\"))";
        return $"r.{accessor}(r.GetOrdinal(\"{f.Name}\"))";
    }

    private static string CastOutputValue(FieldDescriptor f)
    {
        var target = f.ClrType;
        var name = f.Name.TrimStart('@');
        return target switch
        {
            "string" => $"values.TryGetValue(\"{name}\", out var v_{name}) ? (string?)v_{name} ?? string.Empty : string.Empty",
            _ => $"values.TryGetValue(\"{name}\", out var v_{name}) ? ({target})v_{name} : default"
        };
    }

    private static string MaterializeFieldExpressionCached(FieldDescriptor f, int ordinalIndex)
    {
        var accessor = f.ClrType switch
        {
            "int" or "int?" => "GetInt32",
            "long" or "long?" => "GetInt64",
            "short" or "short?" => "GetInt16",
            "byte" or "byte?" => "GetByte",
            "bool" or "bool?" => "GetBoolean",
            "decimal" or "decimal?" => "GetDecimal",
            "double" or "double?" => "GetDouble",
            "float" or "float?" => "GetFloat",
            "DateTime" or "DateTime?" => "GetDateTime",
            "Guid" or "Guid?" => "GetGuid",
            _ => null
        };
        // Determine default fallback expression if ordinal not found
        string defaultExpr = f.ClrType switch
        {
            "string" => "string.Empty",
            "string?" => "null",
            var t when t == "byte[]" => "System.Array.Empty<byte>()",
            var t when t.EndsWith("?") => "null",
            _ => $"default({f.ClrType})"
        };
        if (accessor == null)
        {
            if (f.ClrType.StartsWith("byte[]"))
                return $"o{ordinalIndex} < 0 ? {defaultExpr} : (r.IsDBNull(o{ordinalIndex}) ? System.Array.Empty<byte>() : (byte[])r.GetValue(o{ordinalIndex}))";
            if (f.ClrType == "string")
                return $"o{ordinalIndex} < 0 ? {defaultExpr} : (r.IsDBNull(o{ordinalIndex}) ? string.Empty : r.GetString(o{ordinalIndex}))";
            if (f.ClrType == "string?")
                return $"o{ordinalIndex} < 0 ? {defaultExpr} : (r.IsDBNull(o{ordinalIndex}) ? null : r.GetString(o{ordinalIndex}))";
            return $"o{ordinalIndex} < 0 ? {defaultExpr} : r.GetValue(o{ordinalIndex})";
        }
        var nullable = f.IsNullable && !f.ClrType.EndsWith("?") ? true : f.ClrType.EndsWith("?");
        if (nullable)
            return $"o{ordinalIndex} < 0 ? {defaultExpr} : (r.IsDBNull(o{ordinalIndex}) ? null : ({f.ClrType})r.{accessor}(o{ordinalIndex}))";
        return $"o{ordinalIndex} < 0 ? {defaultExpr} : r.{accessor}(o{ordinalIndex})";
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        // 1. Zerlege anhand üblicher Trenner
        var rawParts = input.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (rawParts.Count == 0) return string.Empty;

        // 2. Heuristik: Erhalte vorhandene Großbuchstaben-Sequenzen (CamelCase Fragmente) innerhalb eines Segments.
        //    Beispiel: "WorkflowListAsJson" bleibt unverändert; "workflowListAsJSON" -> "WorkflowListAsJSON" (JSON bleibt groß wenn >=2 kapitalisierte Folgezeichen).
        string NormalizeSegment(string seg)
        {
            if (seg.Length == 0) return seg;
            // Wenn komplett klein -> klassisch kapitalisieren
            if (seg.All(ch => char.IsLetter(ch) ? char.IsLower(ch) : true))
            {
                return char.ToUpperInvariant(seg[0]) + (seg.Length > 1 ? seg.Substring(1) : string.Empty);
            }
            // Wenn komplett groß (<= 4 Zeichen) -> als Akronym übernehmen (z.B. API, SQL, JSON)
            if (seg.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)))
            {
                if (seg.Length <= 4) return seg.ToUpperInvariant();
                // längere komplett große Segmente: nur erste Groß, Rest klein (z.B. WORKFLOW -> Workflow)
                return char.ToUpperInvariant(seg[0]) + seg.Substring(1).ToLowerInvariant();
            }
            // Gemischtes Muster: wir wollen vorhandene Großbuchstaben erhalten, aber sicherstellen dass erste groß ist.
            // Splitte an Übergängen von Klein->Groß um interne Tokens sichtbar zu machen, recombine dann mit korrekter Großschreibung.
            var sb = new System.Text.StringBuilder();
            var token = new System.Text.StringBuilder();
            void FlushToken()
            {
                if (token.Length == 0) return;
                var t = token.ToString();
                if (t.Length <= 4 && t.All(ch => char.IsUpper(ch)))
                {
                    // Akronym beibehalten
                    sb.Append(t.ToUpperInvariant());
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(t[0]) + (t.Length > 1 ? t.Substring(1) : string.Empty));
                }
                token.Clear();
            }
            for (int i = 0; i < seg.Length; i++)
            {
                var ch = seg[i];
                if (!char.IsLetterOrDigit(ch)) { FlushToken(); continue; }
                if (token.Length > 0)
                {
                    var prev = token[token.Length - 1];
                    // Übergang: vorher Klein, jetzt Groß => neues Token (CamelCase Grenze)
                    if (char.IsLetter(prev) && char.IsLower(prev) && char.IsLetter(ch) && char.IsUpper(ch))
                    {
                        FlushToken();
                    }
                    // Übergang: mehrere Großbuchstaben gefolgt von Klein -> trenne vor Klein außer wenn nur ein Groß bisher
                    else if (token.Length >= 2 && token.ToString().All(cc => char.IsUpper(cc)) && char.IsLetter(ch) && char.IsLower(ch))
                    {
                        // Akronym abgeschlossen
                        FlushToken();
                    }
                }
                token.Append(ch);
            }
            FlushToken();
            var result = sb.ToString();
            if (result.Length == 0)
                result = char.ToUpperInvariant(seg[0]) + (seg.Length > 1 ? seg.Substring(1).ToLowerInvariant() : string.Empty);
            return result;
        }

        var normalizedParts = rawParts.Select(NormalizeSegment).ToList();
        var candidate = string.Concat(normalizedParts);
        candidate = new string(candidate.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        if (string.IsNullOrEmpty(candidate)) candidate = "Schema";
        if (char.IsDigit(candidate[0])) candidate = "N" + candidate;
        return candidate;
    }

    private static readonly Regex MultiBlankLines = new("(\r?\n){3,}", RegexOptions.Compiled);
    private static string NormalizeWhitespace(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        // Collapse 3+ consecutive newlines to exactly two (i.e., one blank line)
        code = MultiBlankLines.Replace(code, match => match.Value.StartsWith("\r\n\r\n") ? "\r\n\r\n" : "\n\n");
        // Ensure exactly one trailing newline
        if (!code.EndsWith("\n")) code += Environment.NewLine;
        return code;
    }

    // Build a result set record type name without the trailing 'Result' suffix.
    private static string SanitizeType(string procPart, string baseName)
    {
        // Use full ResultSet naming (includes 'Result' suffix) to align with existing tests expecting this suffix.
        return NamePolicy.ResultSet(procPart, baseName);
    }

    private static string AliasToIdentifier(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return "_";
        // Behalte Original-Casing; ersetze ungültige Zeichen durch '_'
        var sb = new System.Text.StringBuilder(alias.Length);
        for (int i = 0; i < alias.Length; i++)
        {
            var ch = alias[i];
            if (i == 0)
            {
                if (char.IsLetter(ch) || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
            else
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
                else sb.Append('_');
            }
        }
        var ident = sb.ToString();
        if (string.IsNullOrEmpty(ident)) ident = "_";
        // Falls erstes Zeichen Ziffer -> Prefix '_'
        if (char.IsDigit(ident[0])) ident = "_" + ident;
        // C# reservierte Schlüsselwörter escapen mit '@'
        if (IsCSharpKeyword(ident)) ident = "@" + ident;
        return ident;
    }

    private static readonly HashSet<string> CSharpKeywords = new(new[]
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long","namespace","new","null","object","operator","out","override","params","private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
    }, StringComparer.Ordinal);

    private static bool IsCSharpKeyword(string ident) => CSharpKeywords.Contains(ident);
}
