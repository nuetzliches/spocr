using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using SpocR.DataContext;
using SpocR.DataContext.Queries;
using SpocR.Services;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.Services;

/// <summary>
/// Collects function metadata (scalar + TVF) and populates the SchemaSnapshot.Functions list.
/// </summary>
public sealed class FunctionSnapshotCollector
{
    private readonly DbContext _db;
    private readonly SchemaSnapshotFileLayoutService _layoutService;
    private readonly IConsoleService _console;

    public FunctionSnapshotCollector(DbContext db, SchemaSnapshotFileLayoutService layoutService, IConsoleService console)
    {
        _db = db;
        _layoutService = layoutService;
        _console = console;
    }

    public async Task CollectAsync(SchemaSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot == null) return;
        try
        {
            var fnRows = await _db.FunctionListAsync(ct);
            var paramRows = await _db.FunctionParametersAsync(ct);
            var colRows = await _db.FunctionTvfColumnsAsync(ct);
            var depRows = await _db.FunctionDependenciesAsync(ct);
            if (fnRows == null || fnRows.Count == 0)
            {
                _console.Verbose($"[fn-empty] functions=0 params={(paramRows?.Count ?? 0)} tvfcols={(colRows?.Count ?? 0)}");
                return;
            }
            _console.Verbose($"[fn-raw] functions={fnRows.Count} params={(paramRows?.Count ?? 0)} tvfcols={(colRows?.Count ?? 0)} deps={(depRows?.Count ?? 0)}");

            // Row models dynamic: we rely on dynamic object with properties matching aliases
            var functions = new List<SnapshotFunction>();
            foreach (var r in fnRows)
            {
                string schema = r.schema_name;
                string name = r.function_name;
                string typeCode = r.type_code; // FN / IF / TF
                string definition = r.definition;
                int objectId = r.object_id;
                bool isTableValued = typeCode == "IF" || typeCode == "TF";

                bool encrypted = string.IsNullOrWhiteSpace(definition);
                if (encrypted)
                {
                    _console.Verbose($"[fn-encrypted] {schema}.{name}");
                }

                // JSON Flags jetzt ausschließlich über AST bestimmt (Regex-Fallback entfernt).

                // ReturnSqlType zunächst leer; wird später aus erstem Parameter (Rückgabe-Parameter) rekonstruiert oder via DECLARE/RETURNS Parsing
                var fnModel = new SnapshotFunction
                {
                    Schema = schema,
                    Name = name,
                    IsTableValued = isTableValued ? true : null, // false wird später gepruned
                    ReturnSqlType = string.Empty,
                    ReturnsJson = null,
                    ReturnsJsonArray = null,
                    JsonRootProperty = null,
                    IsEncrypted = encrypted ? true : null
                };

                // JSON Columns für skalare Funktionen: ausschließlich AST (JsonFunctionAstExtractor)
                if (!isTableValued && !encrypted && !string.IsNullOrWhiteSpace(definition))
                {
                    List<SnapshotFunctionColumn> cols = null;
                    try
                    {
                        var ast = new JsonFunctionAstExtractor().Parse(definition);
                        if (ast.ReturnsJson)
                        {
                            // Überschreibe ggf. Array/Root basierend auf AST falls besser bestimmt
                            fnModel.ReturnsJson = true;
                            fnModel.ReturnsJsonArray = ast.ReturnsJsonArray;
                            if (!string.IsNullOrWhiteSpace(ast.JsonRoot)) fnModel.JsonRootProperty = ast.JsonRoot;
                            if (ast.Columns.Count > 0)
                            {
                                SnapshotFunctionColumn Map(JsonFunctionAstColumn c)
                                {
                                    // Keine Heuristik: nur Struktur übernehmen, Typ offen lassen (oder 'json' bei verschachtelten Strukturen).
                                    var mapped = new SnapshotFunctionColumn
                                    {
                                        Name = c.Name,
                                        SqlTypeName = (c.IsNestedJson || c.ReturnsJson) ? "json" : null,
                                        IsNullable = null,
                                        MaxLength = null,
                                        IsNestedJson = c.IsNestedJson ? true : null,
                                        ReturnsJson = c.ReturnsJson ? true : null,
                                        ReturnsJsonArray = c.ReturnsJsonArray,
                                        Columns = new List<SnapshotFunctionColumn>()
                                    };
                                    if (c.Children != null && c.Children.Count > 0)
                                    {
                                        foreach (var child in c.Children)
                                        {
                                            mapped.Columns.Add(Map(child));
                                        }
                                    }
                                    return mapped;
                                }
                                cols = ast.Columns.Select(Map).ToList();
                            }
                        }
                    }
                    catch (Exception astEx)
                    {
                        _console.Verbose($"[fn-ast-error] {schema}.{name} {astEx.Message}");
                    }
                    // Kein Regex-Fallback mehr – bleibt null wenn AST keine JSON Struktur liefert.
                    if (cols != null && cols.Count > 0)
                    {
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var finalList = new List<SnapshotFunctionColumn>();
                        foreach (var col in cols)
                        {
                            var baseName = col.Name;
                            var attempt = baseName;
                            int suffix = 1;
                            while (seen.Contains(attempt))
                            {
                                attempt = baseName + suffix.ToString();
                                suffix++;
                            }
                            col.Name = attempt;
                            seen.Add(attempt);
                            finalList.Add(col);
                        }
                        fnModel.Columns = finalList;
                        _console.Verbose($"[fn-json-cols] {schema}.{name} count={finalList.Count} source=ast");
                    }
                    else
                    {
                        _console.Verbose($"[fn-json-cols-empty] {schema}.{name}");
                    }
                }

                functions.Add(fnModel);
            }

            // Build objectId map for efficient attach

            var idMap = new Dictionary<int, SnapshotFunction>();
            foreach (var fnr in fnRows)
            {
                int id = fnr.object_id;
                string schema = fnr.schema_name;
                string name = fnr.function_name;
                var fn = functions.First(f => f.Name == name && f.Schema == schema);
                idMap[id] = fn;
            }
            foreach (var p in paramRows)
            {
                int objectId = p.object_id;
                if (!idMap.TryGetValue(objectId, out var fn)) continue;
                int ordinal = p.ordinal;
                string rawName = (p.param_name as string) ?? string.Empty;
                string name = rawName.TrimStart('@');
                string sqlType = p.system_type_name;
                int maxLength = p.normalized_length;
                bool isOutput = p.is_output == 1;
                bool isNullable = p.is_nullable == 1;
                bool hasDefault = p.has_default_value == 1;

                // Erkennung Rückgabe-Parameter: Bei skalarer Funktion liefert SQL Server einen Parameter ohne Namen ("" oder null) als Rückgabepseudo-Parameter
                if (!(fn.IsTableValued ?? false) && (string.IsNullOrWhiteSpace(name)))
                {
                    fn.ReturnSqlType = sqlType;
                    fn.ReturnMaxLength = maxLength > 0 ? maxLength : null;
                    if (isNullable) fn.ReturnIsNullable = true; // nur setzen wenn true (false wird weggelassen)
                    continue; // nicht als regulärer Parameter aufnehmen
                }

                fn.Parameters.Add(new SnapshotFunctionParameter
                {
                    Name = name,
                    TableTypeSchema = null,
                    TableTypeName = null,
                    IsOutput = null, // pruned (immer false)
                    SqlTypeName = sqlType,
                    IsNullable = isNullable ? true : null,
                    MaxLength = maxLength > 0 ? maxLength : null,
                    HasDefaultValue = hasDefault ? true : null
                });
            }

            // TVF columns
            foreach (var c in colRows)
            {
                int objectId = c.object_id;
                if (!idMap.TryGetValue(objectId, out var fn)) continue;
                if (!(fn.IsTableValued ?? false)) continue;
                string name = c.column_name;
                string sqlType = c.system_type_name;
                bool isNullable = c.is_nullable == 1;
                int maxLength = c.normalized_length;
                // Collision handling (Name, Name1, Name2 ...)
                var existingNames = new HashSet<string>(fn.Columns.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                string finalName = name;
                if (existingNames.Contains(finalName))
                {
                    int suffix = 1;
                    while (existingNames.Contains(finalName))
                    {
                        finalName = name + suffix.ToString();
                        suffix++;
                    }
                }
                fn.Columns.Add(new SnapshotFunctionColumn
                {
                    Name = finalName,
                    SqlTypeName = sqlType,
                    IsNullable = isNullable ? true : null,
                    MaxLength = maxLength > 0 ? maxLength : null
                });
            }

            // Dependencies (direct) mapping
            if (depRows != null && depRows.Count > 0)
            {
                // Build quick lookup objectId -> (schema,name)
                var metaById = fnRows.ToDictionary(r => r.object_id, r => (schema: (string)r.schema_name, name: (string)r.function_name));
                var depsGrouped = depRows.GroupBy(d => d.referencing_id);
                foreach (var grp in depsGrouped)
                {
                    if (!idMap.TryGetValue(grp.Key, out var fn)) continue;
                    var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var dr in grp)
                    {
                        if (dr.referenced_id == grp.Key) continue; // self-reference skip
                        if (!metaById.TryGetValue(dr.referenced_id, out var target)) continue;
                        list.Add(target.schema + "." + target.name);
                    }
                    if (list.Count > 0)
                    {
                        fn.Dependencies = list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                    else
                    {
                        fn.Dependencies = new List<string>(); // will serialize as [] (decide if prune)
                    }
                }
                _console.Verbose($"[fn-deps] functions_with_deps={snapshot.Functions.Count(f => f.Dependencies != null && f.Dependencies.Count > 0)}");
            }

            // Sorting and version tag
            snapshot.FunctionsVersion = 2; // Nested JSON / erweitertes Model
            // Enrichment: Typableitung für JSON-Spalten aus Parameter- und Tabellen-Metadaten (nur wenn keine SqlTypeName vorhanden)
            try
            {
                // Baue schnelle Lookup Maps für Parameter je Funktion (nutze lokale 'functions' Liste, nicht bereits existierende snapshot.Functions)
                var paramLookup = functions.ToDictionary(f => f.Schema + "." + f.Name, f => f.Parameters.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                // Table Lookup: Schema.Table -> Columns (Name->(SqlType, IsNullable, MaxLength))
                var tableLookup = new Dictionary<string, Dictionary<string, (string SqlType, bool? IsNullable, int? MaxLength)>>(StringComparer.OrdinalIgnoreCase);
                if (snapshot.Tables != null)
                {
                    foreach (var t in snapshot.Tables)
                    {
                        var key = (t.Schema + "." + t.Name);
                        var colMap = new Dictionary<string, (string, bool?, int?)>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in t.Columns ?? new List<SnapshotTableColumn>())
                        {
                            if (!string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.SqlTypeName))
                            {
                                colMap[c.Name] = (c.SqlTypeName!, c.IsNullable, c.MaxLength);
                            }
                        }
                        tableLookup[key] = colMap;
                    }
                }
                foreach (var f in functions.Where(fn => fn.ReturnsJson == true && fn.Columns != null && fn.Columns.Count > 0))
                {
                    var fnKey = f.Schema + "." + f.Name;
                    paramLookup.TryGetValue(fnKey, out var fnParams);
                    foreach (var col in f.Columns)
                    {
                        EnrichColumnRecursive(f, col, fnParams, tableLookup);
                    }
                }
            }
            catch (Exception enrichEx)
            {
                _console.Verbose($"[fn-json-enrich-error] {enrichEx.Message}");
            }
            // Prune empty dependency lists for cleaner JSON (set null so JsonIgnoreCondition prunes)
            foreach (var f in functions)
            {
                if (f.Dependencies != null && f.Dependencies.Count == 0) f.Dependencies = null;
            }

            snapshot.Functions = functions
                .OrderBy(f => f.Schema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _console.Verbose($"[fn-collect] loaded={snapshot.Functions.Count}");
        }
        catch (Exception ex)
        {
            _console.Warn($"[fn-collect-error] {ex.Message}");
        }
    }

    // Hinweis: Alle regex-basierten JSON Rückfall-Heuristiken entfernt (InferJsonColumns & verwandte Methoden).

    private void EnrichColumnRecursive(SnapshotFunction fn, SnapshotFunctionColumn col, Dictionary<string, SnapshotFunctionParameter>? paramMap,
        Dictionary<string, Dictionary<string, (string SqlType, bool? IsNullable, int? MaxLength)>> tableLookup)
    {
        // Wenn bereits konkreter Typ vorhanden und != 'json' (Container) -> nichts tun
        if (!string.IsNullOrWhiteSpace(col.SqlTypeName) && !string.Equals(col.SqlTypeName, "json", StringComparison.OrdinalIgnoreCase))
        {
            if (col.Columns != null && col.Columns.Count > 0)
            {
                foreach (var child in col.Columns)
                    EnrichColumnRecursive(fn, child, paramMap, tableLookup);
            }
            return;
        }
        var segments = col.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var leaf = segments.LastOrDefault() ?? col.Name;
        SnapshotFunctionParameter? matchedParam = null;
        if (paramMap != null)
        {
            if (paramMap.TryGetValue(leaf, out var direct)) matchedParam = direct;
            else
            {
                foreach (var p in paramMap.Values)
                {
                    if (p.Name.EndsWith(leaf, StringComparison.OrdinalIgnoreCase)) { matchedParam = p; break; }
                }
            }
        }
        if (matchedParam != null && !string.IsNullOrWhiteSpace(matchedParam.SqlTypeName))
        {
            col.SqlTypeName = matchedParam.SqlTypeName;
            col.IsNullable = matchedParam.IsNullable;
            col.MaxLength = matchedParam.MaxLength;
        }
        else
        {
            // Zusatz: dateTime Alias -> CreatedDt / UpdatedDt anhand erstes Segment
            if (leaf.Equals("dateTime", StringComparison.OrdinalIgnoreCase) && paramMap != null)
            {
                string paramAlias = segments.Length > 0 && segments[0].Equals("updated", StringComparison.OrdinalIgnoreCase) ? "UpdatedDt" : "CreatedDt";
                if (paramMap.TryGetValue(paramAlias, out var dtParam) && !string.IsNullOrWhiteSpace(dtParam.SqlTypeName))
                {
                    col.SqlTypeName = dtParam.SqlTypeName;
                    col.IsNullable = dtParam.IsNullable;
                    col.MaxLength = dtParam.MaxLength;
                }
            }
            // DisplayName / Initials / userId / rowVersion mapping
            if (string.IsNullOrWhiteSpace(col.SqlTypeName))
            {
                if (leaf.Equals("displayName", StringComparison.OrdinalIgnoreCase))
                {
                    TryMapTableColumn("identity.User", "DisplayName", col, tableLookup); // direkter Name, falls vorhanden
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName))
                    {
                        TryMapTableColumn("identity.User", "UserName", col, tableLookup); // Fallback
                    }
                }
                else if (leaf.Equals("initials", StringComparison.OrdinalIgnoreCase))
                {
                    TryMapTableColumn("identity.User", "Initials", col, tableLookup);
                }
                else if (leaf.Equals("userId", StringComparison.OrdinalIgnoreCase))
                {
                    TryMapTableColumn("identity.User", "UserId", col, tableLookup);
                }
                else if (leaf.Equals("rowVersion", StringComparison.OrdinalIgnoreCase))
                {
                    // Versuche echte Tabellen-Spalte zuzuordnen (identity.User.RowVersion) und nutze deren Typ.
                    TryMapTableColumn("identity.User", "RowVersion", col, tableLookup);
                    // Fallback falls Mapping leer blieb: verwende kanonischen SqlTypeName 'rowversion'.
                    if (string.IsNullOrWhiteSpace(col.SqlTypeName)) col.SqlTypeName = "rowversion";
                }
            }
            // Generischer Fallback: identity.User.<leaf>
            if (string.IsNullOrWhiteSpace(col.SqlTypeName))
            {
                TryMapTableColumn("identity.User", leaf, col, tableLookup);
            }
        }
        if (col.Columns != null && col.Columns.Count > 0)
        {
            foreach (var child in col.Columns)
                EnrichColumnRecursive(fn, child, paramMap, tableLookup);
        }
    }

    private void TryMapTableColumn(string tableKey, string columnName, SnapshotFunctionColumn target,
        Dictionary<string, Dictionary<string, (string SqlType, bool? IsNullable, int? MaxLength)>> tableLookup)
    {
        if (tableLookup.TryGetValue(tableKey, out var cols) && cols.TryGetValue(columnName, out var meta))
        {
            if (string.IsNullOrWhiteSpace(target.SqlTypeName)) target.SqlTypeName = meta.SqlType;
            if (!target.IsNullable.HasValue) target.IsNullable = meta.IsNullable;
            if (!target.MaxLength.HasValue) target.MaxLength = meta.MaxLength;
        }
    }
}
