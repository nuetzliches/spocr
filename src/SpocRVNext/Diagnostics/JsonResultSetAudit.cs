using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpocR.SpocRVNext.Metadata;

namespace SpocR.SpocRVNext.Diagnostics;

/// <summary>
/// Audits JSON result sets for potential weak typing (string placeholders where numeric/bool/datetime could be inferred).
/// Writes a report file under debug/json-audit.txt.
/// </summary>
public static class JsonResultSetAudit
{
    public sealed record AuditFinding(string Procedure, string ResultSet, string Field, string SqlType, string ClrType, string Suggested);

    private static readonly HashSet<string> _numeric = new(StringComparer.OrdinalIgnoreCase)
    { "int","bigint","smallint","tinyint","decimal","numeric","money","smallmoney","float","real" };
    private static readonly HashSet<string> _datetime = new(StringComparer.OrdinalIgnoreCase)
    { "date","datetime","datetime2","smalldatetime","datetimeoffset","time" };
    private static readonly HashSet<string> _bool = new(StringComparer.OrdinalIgnoreCase) { "bit" };
    private static readonly HashSet<string> _guid = new(StringComparer.OrdinalIgnoreCase) { "uniqueidentifier" };

    public static IReadOnlyList<AuditFinding> Run(IEnumerable<ProcedureDescriptor> procedures)
    {
        var findings = new List<AuditFinding>();
        foreach (var p in procedures)
        {
            foreach (var rs in p.ResultSets.Where(r => r.JsonPayload != null))
            {
                foreach (var f in rs.Fields)
                {
                    var sql = f.SqlTypeName ?? string.Empty;
                    var clr = f.ClrType;
                    if (string.IsNullOrWhiteSpace(sql)) continue; // skip unknown
                    // normalize core type
                    var core = sql.ToLowerInvariant();
                    var paren = core.IndexOf('(');
                    if (paren >= 0) core = core.Substring(0, paren);
                    string? suggested = null;
                    if (_numeric.Contains(core)) suggested = core switch
                    {
                        "bigint" => "long",
                        "int" => "int",
                        "smallint" => "short",
                        "tinyint" => "byte",
                        "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
                        "float" => "double",
                        "real" => "float",
                        _ => null
                    };
                    else if (_datetime.Contains(core)) suggested = core == "datetimeoffset" ? "DateTimeOffset" : core == "time" ? "TimeSpan" : "DateTime";
                    else if (_bool.Contains(core)) suggested = "bool";
                    else if (_guid.Contains(core)) suggested = "Guid";

                    if (suggested != null && clr.StartsWith("string", StringComparison.Ordinal))
                    {
                        findings.Add(new AuditFinding(p.OperationName, rs.Name, f.PropertyName, sql, clr, suggested));
                    }
                }
            }
        }
        return findings;
    }

    public static void WriteReport(string rootDir, IEnumerable<ProcedureDescriptor> procedures)
    {
        var findings = Run(procedures);
        var path = Path.Combine(rootDir, "debug", "json-audit.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var sw = new StreamWriter(path, false);
        sw.WriteLine("# JSON ResultSet Type Audit");
        sw.WriteLine("Generated: " + DateTime.UtcNow.ToString("u"));
        sw.WriteLine("Total Findings: " + findings.Count);
        foreach (var f in findings)
        {
            sw.WriteLine($"{f.Procedure}|{f.ResultSet}|{f.Field}|sql={f.SqlType}|clr={f.ClrType}|suggest={f.Suggested}");
        }
    }
}
