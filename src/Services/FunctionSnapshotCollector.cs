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

                // JSON Flags werden primär über AST bestimmt; Regex-Erkennung nur als Fallback falls AST kein FOR JSON erkennt.
                bool? returnsJson = null; // nur gesetzt falls Regex-Fallback greift (für Logging Quelle)

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

                // JSON Columns für skalare Funktionen: immer AST versuchen, erst danach Regex-Fallback
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
                    if (cols == null)
                    {
                        try
                        {
                            // Regex-Fallback nur wenn AST nichts ergab
                            var pattern = @"FOR\s+JSON\s+(PATH|AUTO)?(?<rest>[^;]*)";
                            var match = System.Text.RegularExpressions.Regex.Match(definition, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                fnModel.ReturnsJson = true;
                                var rest = match.Groups["rest"].Value;
                                if (!string.IsNullOrEmpty(rest))
                                {
                                    if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"WITHOUT\s+ARRAY\s+WRAPPER", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                    {
                                        fnModel.ReturnsJsonArray = false;
                                    }
                                    else fnModel.ReturnsJsonArray = true;
                                    var rootMatch = System.Text.RegularExpressions.Regex.Match(rest, @"ROOT\s*\(\s*'([^']+)'\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (rootMatch.Success) fnModel.JsonRootProperty = rootMatch.Groups[1].Value;
                                }
                                var inferred = InferJsonColumns(definition, schema, name);
                                cols = inferred;
                            }
                        }
                        catch (Exception fallbackEx)
                        {
                            _console.Verbose($"[fn-json-fallback-error] {schema}.{name} {fallbackEx.Message}");
                        }
                    }
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
                        _console.Verbose($"[fn-json-cols] {schema}.{name} count={finalList.Count} source={(fnModel.ReturnsJsonArray!=null && returnsJson==null ? "ast" : (returnsJson==true?"regex":"ast"))}");
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

    /// <summary>
    /// Heuristische Extraktion der SELECT-Liste für RETURN (SELECT ... FOR JSON ...) einer skalaren JSON Funktion.
    /// Liefert SnapshotFunctionColumn Einträge mit groben Typ-Angaben (json / nvarchar(max) / cast-Zieltyp / convert-Zieltyp).
    /// Grenzen: Keine vollständige SQL AST, verschachtelte Subqueries können die Regex brechen; bei Fehlschlag wird leere Liste zurückgegeben.
    /// </summary>
    private static List<SnapshotFunctionColumn> InferJsonColumns(string definition, string schema, string name)
    {
        // Rekursive, heuristische Analyse von SELECT Listen für FOR JSON Konstrukte.
        var result = new List<SnapshotFunctionColumn>();
        if (string.IsNullOrWhiteSpace(definition)) return result;
        try
        {
            var match = Regex.Match(definition, @"SELECT\s+(?<select>.+?)\s+FROM\s+.+?FOR\s+JSON", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return result;
            var selectList = match.Groups["select"].Value;
            if (string.IsNullOrWhiteSpace(selectList)) return result;

            var segments = SplitTopLevel(selectList);
            foreach (var rawSegment in segments)
            {
                var seg = rawSegment.Trim();
                if (string.IsNullOrWhiteSpace(seg)) continue;

                var col = ParseSegmentToColumn(seg, depth:0);
                if (col != null) result.Add(col);
            }
        }
        catch { }
        return result;
    }

    private static SnapshotFunctionColumn ParseSegmentToColumn(string segment, int depth)
    {
        if (depth > 3) return null; // Tiefe begrenzen
        string seg = segment.Trim();
        string alias = null;
        string expression = seg;
        var assignMatch = Regex.Match(seg, @"^(?<alias>\[[^]]+\]|'[A-Za-z0-9_.]+'|[A-Za-z0-9_]+)\s*=\s*(?<expr>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (assignMatch.Success)
        {
            alias = StripBrackets(assignMatch.Groups["alias"].Value.Trim('\''));
            expression = assignMatch.Groups["expr"].Value;
        }
        else
        {
            var asMatch = Regex.Match(seg, @"^(?<expr>.+?)\s+AS\s+(?<alias>\[[^]]+\]|'[A-Za-z0-9_.]+'|[A-Za-z0-9_]+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (asMatch.Success)
            {
                alias = StripBrackets(asMatch.Groups["alias"].Value.Trim('\''));
                expression = asMatch.Groups["expr"].Value;
            }
            else
            {
                var implicitQuoted = Regex.Match(seg, @"^(?<expr>.+?)\s+'(?<alias>[A-Za-z0-9_.]+)'$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (implicitQuoted.Success)
                {
                    alias = implicitQuoted.Groups["alias"].Value;
                    expression = implicitQuoted.Groups["expr"].Value;
                }
            }
        }
        if (alias == null)
        {
            var cleaned = Regex.Replace(expression, @"^(CAST|CONVERT)\s*\(", "", RegexOptions.IgnoreCase).Trim();
            var dotIdx = cleaned.LastIndexOf('.');
            if (dotIdx >= 0 && dotIdx < cleaned.Length - 1)
            {
                alias = StripBrackets(cleaned.Substring(dotIdx + 1).Trim());
            }
            else
            {
                alias = StripBrackets(cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "col");
            }
        }
        alias = alias?.Trim();
        if (string.IsNullOrWhiteSpace(alias)) alias = "col" + depth.ToString();

        // Nested JSON erkennen
        bool hasNestedSelectJson = Regex.IsMatch(expression, @"SELECT.+FOR\s+JSON", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        bool hasJsonQuery = Regex.IsMatch(expression, @"JSON_QUERY\s*\(", RegexOptions.IgnoreCase);
        bool isNested = hasNestedSelectJson || hasJsonQuery;

        string sqlType = null; int maxLen = 0; bool isNullable = true;
        if (isNested)
        {
            sqlType = "json"; maxLen = -1;
        }
        else
        {
            var castType = Regex.Match(expression, @"CAST\s*\(.+?\s+AS\s+(?<type>[A-Za-z0-9_]+(\(max\)|\(\d+\))?)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (castType.Success) sqlType = castType.Groups["type"].Value; else {
                var convType = Regex.Match(expression, @"CONVERT\s*\(\s*(?<type>[A-Za-z0-9_]+(\(max\)|\(\d+\))?)\s*,", RegexOptions.IgnoreCase);
                if (convType.Success) sqlType = convType.Groups["type"].Value;
            }
            if (sqlType == null)
            {
                if (Regex.IsMatch(expression, @"JSON_VALUE", RegexOptions.IgnoreCase)) sqlType = "nvarchar(max)"; else sqlType = "nvarchar(max)";
            }
            maxLen = sqlType.Contains("(max)", StringComparison.OrdinalIgnoreCase) || sqlType.EndsWith("max", StringComparison.OrdinalIgnoreCase) ? -1 : ExtractLength(sqlType);
        }

        var col = new SnapshotFunctionColumn
        {
            Name = alias,
            SqlTypeName = sqlType,
            IsNullable = isNullable,
            MaxLength = maxLen,
            IsNestedJson = isNested ? true : null,
            ReturnsJson = isNested ? true : null,
            ReturnsJsonArray = isNested && !Regex.IsMatch(expression, @"WITHOUT\s+ARRAY\s+WRAPPER", RegexOptions.IgnoreCase) ? true : (isNested ? (bool?)false : null)
        };

        // Rekursion: falls verschachteltes SELECT vorhanden, versuche innere SELECT Liste zu extrahieren
        if (hasNestedSelectJson)
        {
            var innerMatch = Regex.Match(expression, @"SELECT\s+(?<select>.+?)\s+FROM\s+.+?FOR\s+JSON", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (innerMatch.Success)
            {
                var innerSelect = innerMatch.Groups["select"].Value;
                var innerSegments = SplitTopLevel(innerSelect);
                foreach (var rawInner in innerSegments)
                {
                    var innerSeg = rawInner.Trim(); if (string.IsNullOrWhiteSpace(innerSeg)) continue;
                    var child = ParseSegmentToColumn(innerSeg, depth + 1);
                    if (child != null) col.Columns.Add(child);
                }
                if (col.Columns.Count == 0) col.Columns = new List<SnapshotFunctionColumn>(); // leer -> später gepruned
            }
        }
        return col;
    }

    private static List<string> SplitTopLevel(string selectList)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(selectList)) return list;
        int depth = 0;
        int start = 0;
        for (int i = 0; i < selectList.Length; i++)
        {
            char c = selectList[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                list.Add(selectList.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (start < selectList.Length)
        {
            list.Add(selectList.Substring(start));
        }
        return list;
    }

    private static string StripBrackets(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        value = value.Trim();
        if (value.StartsWith("[") && value.EndsWith("]") && value.Length > 2)
        {
            return value.Substring(1, value.Length - 2);
        }
        return value;
    }

    private static int ExtractLength(string sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return 0;
        var m = Regex.Match(sqlType, @"\((?<len>\d+)\)");
        if (m.Success && int.TryParse(m.Groups["len"].Value, out var len)) return len;
        return 0;
    }
}
