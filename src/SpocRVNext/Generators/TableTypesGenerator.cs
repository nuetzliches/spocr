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
        string? tableTypeTemplate = null;
        if (_loader != null && _loader.TryLoad("TableType", out var tpl)) tableTypeTemplate = tpl;

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
        foreach (var tt in types.OrderBy(t => t.Schema).ThenBy(t => t.Name))
        {
            var schemaPascal = ToPascalCase(tt.Schema);
            var schemaDir = Path.Combine(rootOut, schemaPascal);
            Directory.CreateDirectory(schemaDir);
            var cols = tt.Columns.Select(c => new
            {
                c.Name,
                PropertyName = Sanitize(c.Name),
                ClrType = MapSqlToClr(c.SqlType, c.IsNullable),
            }).ToList();
            // Preserve original snapshot name (only sanitize). No suffix enforcement; snapshot already ensures uniqueness.
            var typeName = Sanitize(tt.Name);
            var model = new
            {
                Namespace = ns + "." + schemaPascal,
                Schema = schemaPascal,
                Name = tt.Name,
                TypeName = typeName,
                TableTypeName = tt.Name, // original SQL UDTT name
                Columns = cols.Select((c, idx) => new { c.PropertyName, c.ClrType, Separator = idx == cols.Count - 1 ? string.Empty : "," }).ToList(),
                ColumnsCount = cols.Count,
                GeneratedAt = DateTime.UtcNow.ToString("O")
            };
            string code;
            if (tableTypeTemplate != null)
            {
                var extendedModel = new { model.Namespace, model.Schema, model.Name, model.TypeName, model.TableTypeName, model.Columns, model.ColumnsCount, model.GeneratedAt, HEADER = header };
                code = _renderer.Render(tableTypeTemplate, extendedModel);
            }
            else
            {
                // Inline fallback: ensure single suffix and schema namespace
                code = header + RenderInline(ns, tt.Schema, tt.Name, tt.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable, c.MaxLength)).ToArray());
            }
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

    private static string Sanitize(string input)
    {
        var s = new string(input.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        if (string.IsNullOrWhiteSpace(s)) s = "TableType";
        if (char.IsDigit(s[0])) s = "N" + s;
        return s;
    }

    private static string RenderInline(string ns, string schema, string baseName, (string col, string sql, bool nullable, int? max)[] cols)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Generated by SpocR vNext TableTypesGenerator");
        sb.AppendLine($"namespace {ns}.{schema};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// User Defined Table Type {schema}.{baseName}TableType");
        sb.AppendLine("/// <remarks>Generated at " + DateTime.UtcNow.ToString("O") + "</remarks>");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public readonly record struct {Sanitize(baseName)}TableType(");
        for (int i = 0; i < cols.Length; i++)
        {
            var (col, sql, nullable, max) = cols[i];
            var clr = MapSqlToClr(sql, nullable);
            var comma = i == cols.Length - 1 ? string.Empty : ",";
            sb.Append("    ").Append(clr).Append(' ').Append(Sanitize(col)).Append(comma).AppendLine();
        }
        sb.AppendLine(") : ITableType;");
        return sb.ToString();
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