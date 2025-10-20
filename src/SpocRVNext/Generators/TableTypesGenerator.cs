using System;
using System.IO;
using System.Linq;
using System.Text;
using SpocRVNext.Configuration;
using SpocRVNext.Metadata;
using SpocR.SpocRVNext.Engine;

namespace SpocR.SpocRVNext.Generators;

public sealed class TableTypesGenerator
{
    private readonly EnvConfiguration _cfg;
    private readonly ITableTypeMetadataProvider _provider;
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly string _projectRoot;

    public TableTypesGenerator(EnvConfiguration cfg, ITableTypeMetadataProvider provider, ITemplateRenderer renderer, ITemplateLoader? loader = null, string? projectRoot = null)
    {
        _cfg = cfg;
        _provider = provider;
        _renderer = renderer;
        _loader = loader;
        _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
    }

    public int Generate()
    {
        var types = _provider.GetAll();
        var resolver = new NamespaceResolver(_cfg);
        var resolvedBase = resolver.Resolve();
        var outputDirName = _cfg.OutputDir ?? "SpocR";
        var rootOut = Path.Combine(_projectRoot, outputDirName);
        // Neue Regel: Namespace = <BaseRoot>.<OutputDirName> (kein doppeltes Anhängen), Schema wird pro Datei ergänzt.
        var ns = resolvedBase.EndsWith($".{outputDirName}", StringComparison.Ordinal)
            ? resolvedBase
            : resolvedBase + "." + outputDirName;
        Directory.CreateDirectory(rootOut);
        var written = 0;
        if (_loader == null || !_loader.TryLoad("TableType", out var tableTypeTemplate))
        {
            throw new InvalidOperationException("TableType template 'TableType.spt' not found – generation aborted.");
        }

        // Load optional shared header template
        string header = string.Empty;
        if (_loader != null && _loader.TryLoad("_Header", out var headerTpl))
        {
            header = headerTpl.TrimEnd() + Environment.NewLine; // ensure newline
        }

        // Ensure interface exists (ITableType) and has correct namespace; rewrite if outdated
        var interfacePath = Path.Combine(rootOut, "ITableType.cs");
        bool mustWriteInterface = true;
        if (File.Exists(interfacePath))
        {
            var existing = File.ReadAllText(interfacePath);
            if (existing.Contains($"namespace {ns};"))
            {
                mustWriteInterface = false; // already correct
            }
        }
        if (mustWriteInterface)
        {
            string ifaceCode;
            if (_loader != null && _loader.TryLoad("ITableType", out var ifaceTpl))
            {
                var ifaceModel = new { Namespace = ns, HEADER = header };
                ifaceCode = _renderer.Render(ifaceTpl, ifaceModel);
            }
            else
            {
                ifaceCode = $"{header}namespace {ns};\n\npublic interface ITableType {{}}\n";
            }
            File.WriteAllText(interfacePath, ifaceCode, Encoding.UTF8);
            written++;
        }
        // Central BuildSchemas allow-list now applies to Procedures AND TableTypes (CHECKLIST 2025-10-15).
        if (_cfg.BuildSchemas is { Count: > 0 })
        {
            var beforeT = types.Count;
            types = types.Where(t => _cfg.BuildSchemas.Contains(t.Schema)).ToList();
            var removedT = beforeT - types.Count;
            try { Console.Out.WriteLine($"[spocr vNext] Info: TableTypes allow-list applied -> {types.Count}/{beforeT} retained (removed {removedT}). Schemas: {string.Join(",", _cfg.BuildSchemas)}"); } catch { }
        }
        foreach (var tt in types.OrderBy(t => t.Schema).ThenBy(t => t.Name))
        {
            var schemaPascal = ToPascalCase(tt.Schema);
            var schemaDir = Path.Combine(rootOut, schemaPascal);
            Directory.CreateDirectory(schemaDir);
            var cols = tt.Columns.Select(c => new
            {
                c.Name,
                PropertyName = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeIdentifier(c.Name),
                ClrType = MapSqlToClr(c.SqlType, c.IsNullable),
            }).ToList();
            // Konsistente Naming-Konvention mit Input-Mapping: CLR-Typ = Pascal(TableTypeName) + 'Table'
            var typeName = SpocR.SpocRVNext.Utils.NameSanitizer.SanitizeIdentifier(tt.Name); // kein Suffix hinzufügen
            // Entferne ggf. veraltete Datei mit Suffix 'Table'
            var obsolete = Path.Combine(schemaDir, typeName + "Table.cs");
            if (File.Exists(obsolete))
            {
                try { File.Delete(obsolete); } catch { }
            }
            var model = new
            {
                Namespace = ns + "." + schemaPascal,
                Schema = schemaPascal,
                Name = tt.Name,
                TypeName = typeName,
                TableTypeName = tt.Name, // original SQL UDTT name
                Columns = cols.Select((c, idx) => new { c.PropertyName, c.ClrType, Separator = idx == cols.Count - 1 ? string.Empty : "," }).ToList(),
                ColumnsCount = cols.Count,
                // Deterministischer Platzhalter statt echter Zeit
                GeneratedAt = "<generated>"
            };
            var extendedModel = new { model.Namespace, model.Schema, model.Name, model.TypeName, model.TableTypeName, model.Columns, model.ColumnsCount, model.GeneratedAt, HEADER = header };
            var code = _renderer.Render(tableTypeTemplate, extendedModel);
            var fileName = typeName + ".cs";
            File.WriteAllText(Path.Combine(schemaDir, fileName), code, Encoding.UTF8);
            written++;
        }
        return written; // includes interface (even if 0 types)
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var parts = input.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1).ToLowerInvariant() : string.Empty));
        var candidate = string.Concat(parts);
        candidate = new string(candidate.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        if (string.IsNullOrEmpty(candidate)) candidate = "Schema";
        if (char.IsDigit(candidate[0])) candidate = "N" + candidate;
        return candidate;
    }

    // Sanitization centralized via NameSanitizer.SanitizeIdentifier


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