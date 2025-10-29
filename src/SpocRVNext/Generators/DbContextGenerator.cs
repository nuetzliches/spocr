using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using SpocR.Infrastructure;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;
using SpocR.SpocRVNext.Engine;
using SpocRVNext.Configuration; // for EnvConfiguration & NamespaceResolver
using SpocR.SpocRVNext.Metadata; // ProcedureDescriptor
using System.Collections.Generic;
using System.Text;
using SpocR.SpocRVNext.Utils; // NamePolicy

namespace SpocR.SpocRVNext.Generators;

/// <summary>
/// Generates the DbContext related artifacts from templates (interface, context, options, DI extension, endpoints).
/// Runs unconditionally in the enforced next-only configuration (mode provider remains overridable for tests).
/// </summary>
public class DbContextGenerator
{
    private readonly FileManager<ConfigurationModel>? _legacyConfig;
    private readonly OutputService _outputService;
    private readonly IConsoleService _console;
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly Func<IReadOnlyList<ProcedureDescriptor>> _proceduresProvider;

    public DbContextGenerator(FileManager<ConfigurationModel> configFile, OutputService outputService, IConsoleService console, ITemplateRenderer renderer, ITemplateLoader? loader = null, Func<IReadOnlyList<ProcedureDescriptor>>? proceduresProvider = null)
    {
        _legacyConfig = configFile;
        _outputService = outputService;
        _console = console;
        _renderer = renderer;
        _loader = loader;
        _proceduresProvider = proceduresProvider ?? (() => Array.Empty<ProcedureDescriptor>());
    }

    public Task GenerateAsync(bool isDryRun) => GenerateInternalAsync(isDryRun);

    private async Task GenerateInternalAsync(bool isDryRun)
    {
        // Load .env configuration (primary source) with safe fallback to legacy defaults.
        var projectRoot = DirectoryUtils.GetWorkingDirectory();
        EnvConfiguration envConfig;
        try
        {
            envConfig = EnvConfiguration.Load(projectRoot: projectRoot);
        }
        catch (Exception ex)
        {
            _console.Warn("[dbctx] Failed to load .env configuration: " + ex.Message);
            envConfig = new EnvConfiguration();
        }

        // Resolve namespace from .env first, fall back to legacy configuration.
        string? explicitNs = envConfig.NamespaceRoot?.Trim();
        if (string.IsNullOrWhiteSpace(explicitNs))
        {
            try
            {
                explicitNs = _legacyConfig?.Config.Project.Output.Namespace?.Trim();
            }
            catch (Exception legacyEx)
            {
                _console.Verbose("[dbctx] Legacy namespace fallback failed: " + legacyEx.Message);
            }
        }

        string baseNs;
        if (!string.IsNullOrWhiteSpace(explicitNs))
        {
            baseNs = explicitNs!;
        }
        else
        {
            try
            {
                var resolver = new NamespaceResolver(envConfig, msg => _console.Warn("[dbctx] ns-resolver: " + msg));
                baseNs = resolver.Resolve(projectRoot);
                _console.Verbose($"[dbctx] Derived namespace '{baseNs}' via resolver");
            }
            catch (Exception ex)
            {
                _console.Warn("[dbctx] Unable to determine namespace: " + ex.Message);
                return;
            }
        }

        // Determine output directory using SPOCR_OUTPUT_DIR (defaults to SpocR).
        string outputDir = string.IsNullOrWhiteSpace(envConfig.OutputDir) ? "SpocR" : envConfig.OutputDir.Trim();
        string spocrDir;
        if (Path.IsPathRooted(outputDir))
        {
            spocrDir = Path.GetFullPath(outputDir);
        }
        else
        {
            spocrDir = Path.GetFullPath(Path.Combine(projectRoot, outputDir));
        }

        if (!Directory.Exists(spocrDir) && !isDryRun)
        {
            Directory.CreateDirectory(spocrDir);
        }

        try
        {
            _console.Verbose($"[dbctx] output='{spocrDir}' namespace='{baseNs}' cwd={Directory.GetCurrentDirectory()}");
            if (_legacyConfig != null)
            {
                var dcPath = _legacyConfig.Config.Project.Output.DataContext.Path;
                var dcDir = DirectoryUtils.GetWorkingDirectory(dcPath);
                _console.Verbose($"[dbctx] legacy dcPath='{dcPath ?? "<null>"}' resolved='{dcDir}'");
            }
        }
        catch (Exception diagEx)
        {
            _console.Verbose($"[dbctx] diag-error: {diagEx.Message}");
        }

        // Append .SpocR suffix only if not already present.
        var finalNs = baseNs.EndsWith(".SpocR", StringComparison.Ordinal) ? baseNs : baseNs + ".SpocR";
        // Collect procedure descriptors (may be empty in legacy mode or early pipeline stages)
        var procedures = _proceduresProvider();
        // Build method metadata (naming + signatures) only if we have procedures
        var methodBlocksInterface = new StringBuilder();
        var methodBlocksImpl = new StringBuilder();
        if (procedures.Count > 0)
        {
            _console.Verbose($"[dbctx] Generating {procedures.Count} procedure methods");
            // Naming conflict resolution per spec:
            // 1. Preserve original procedure name part (after schema) converting invalid chars to '_'
            // 2. If collision across schemas, prefix with schema pascal case.
            var nameMap = new Dictionary<string, List<(ProcedureDescriptor Proc, string SchemaPascal, string ProcPart)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in procedures)
            {
                var op = p.OperationName; // e.g. dbo.GetUsers
                var schema = p.Schema ?? "dbo";
                var procPart = op.Contains('.') ? op[(op.IndexOf('.') + 1)..] : op; // raw procedure part
                var normalized = NormalizeProcedurePart(procPart);
                if (!nameMap.TryGetValue(normalized, out var list)) nameMap[normalized] = list = new();
                list.Add((p, ToPascalCase(schema), procPart));
            }
            // Determine final method names
            var finalNames = new Dictionary<ProcedureDescriptor, string>();
            foreach (var kv in nameMap)
            {
                if (kv.Value.Count == 1)
                {
                    finalNames[kv.Value[0].Proc] = kv.Key; // single -> no prefix
                }
                else
                {
                    // collision -> prefix schema pascal
                    foreach (var item in kv.Value)
                    {
                        var candidate = item.SchemaPascal + kv.Key; // Schema + NormalizedProc
                        finalNames[item.Proc] = candidate;
                    }
                }
            }
            // Emit method signatures
            foreach (var p in procedures.OrderBy(p => finalNames[p]))
            {
                var methodName = finalNames[p];
                var schemaPascal = ToPascalCase(p.Schema ?? "dbo");
                var schemaNamespace = finalNs + "." + schemaPascal;
                // Types built by ProceduresGenerator: <ProcPart>Input, <ProcPart>Result, <ProcPart> (static wrapper)
                var procPart = p.OperationName.Contains('.') ? p.OperationName[(p.OperationName.IndexOf('.') + 1)..] : p.OperationName;
                var procedureTypeName = NamePolicy.Procedure(procPart); // matches template
                var unifiedResultTypeName = NamePolicy.Result(procPart);
                var inputTypeName = NamePolicy.Input(procPart);
                var hasInput = p.InputParameters?.Count > 0;
                var resultReturnType = schemaNamespace + "." + unifiedResultTypeName;
                var inputParamSig = hasInput ? schemaNamespace + "." + inputTypeName + " input, " : string.Empty;
                // Interface method
                methodBlocksInterface.AppendLine($"    Task<{resultReturnType}> {methodName}Async({inputParamSig}CancellationToken cancellationToken = default);");
                // Implementation
                methodBlocksImpl.AppendLine("    /// <summary>Executes stored procedure '" + p.Schema + "." + p.ProcedureName + "'</summary>");
                methodBlocksImpl.Append("    public async Task<" + resultReturnType + "> " + methodName + "Async(");
                if (hasInput) methodBlocksImpl.Append(inputParamSig);
                methodBlocksImpl.Append("CancellationToken cancellationToken = default)\n");
                methodBlocksImpl.AppendLine("    {");
                methodBlocksImpl.AppendLine("        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);");
                methodBlocksImpl.Append("        var result = await " + schemaNamespace + "." + procedureTypeName + ".ExecuteAsync(conn");
                if (hasInput) methodBlocksImpl.Append(", input");
                methodBlocksImpl.Append(", cancellationToken).ConfigureAwait(false);\n");
                methodBlocksImpl.AppendLine("        return result;");
                methodBlocksImpl.AppendLine("    }");
                methodBlocksImpl.AppendLine();
            }
        }

        var model = new { Namespace = finalNs, MethodsInterface = methodBlocksInterface.ToString(), MethodsImpl = methodBlocksImpl.ToString() };

        // Generate core artifacts (always)
        await WriteAsync(spocrDir, "ISpocRDbContext.cs", Render("ISpocRDbContext", GetTemplate_Interface(finalNs, model.MethodsInterface), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextOptions.cs", Render("SpocRDbContextOptions", GetTemplate_Options(finalNs), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContext.cs", Render("SpocRDbContext", GetTemplate_Context(finalNs, model.MethodsImpl), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextServiceCollectionExtensions.cs", Render("SpocRDbContextServiceCollectionExtensions", GetTemplate_Di(finalNs), model), isDryRun);

        // Endpoint generation is gated to net10 (forward feature). We evaluate SPOCR_TFM or default major used by template loader.
        var tfm = Environment.GetEnvironmentVariable("SPOCR_TFM");
        var major = ExtractTfmMajor(tfm);
        if (major == "net10")
        {
            await WriteAsync(spocrDir, "SpocRDbContextEndpoints.cs", Render("SpocRDbContextEndpoints", GetTemplate_Endpoints(finalNs), model), isDryRun);
        }
        else
        {
            _console.Verbose($"[dbctx] Skip endpoints (TFM '{tfm ?? "<null>"}' → major '{major ?? "<none>"}' != net10)");
        }
    }

    private static string? ExtractTfmMajor(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm)) return null;
        tfm = tfm.Trim().ToLowerInvariant();
        if (!tfm.StartsWith("net")) return null;
        var digits = new string(tfm.Skip(3).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        return "net" + digits;
    }

    private string Render(string logicalName, SourceText fallback, object model)
    {
        if (_loader != null && _loader.TryLoad(Path.GetFileNameWithoutExtension(logicalName), out var tpl))
        {
            try
            {
                return _renderer.Render(tpl, model);
            }
            catch (Exception ex)
            {
                _console.Warn($"[dbctx] Template render failed for {logicalName}, using fallback. Error: {ex.Message}");
            }
        }
        return fallback.ToString();
    }

    private static SourceText GetTemplate_Interface(string ns, string methods) => SourceText.From($"" +
        "/// <summary>Generated interface for the database context abstraction.</summary>\n" +
        $"namespace {ns};\n\n" +
        "using System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n" +
        "public interface ISpocRDbContext\n" +
        "{\n" +
        "    DbConnection OpenConnection();\n" +
        "    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);\n" +
        "    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);\n" +
        "    int CommandTimeout { get; }\n" +
        (string.IsNullOrWhiteSpace(methods) ? string.Empty : methods) +
        "}\n");

    private static SourceText GetTemplate_Options(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System.Text.Json;\n\n" +
        "public sealed class SpocRDbContextOptions\n{\n" +
        "    public string? ConnectionString { get; set; }\n" +
        "    public string? ConnectionStringName { get; set; }\n" +
    "    public int? CommandTimeout { get; set; }\n" +
        "    public int? MaxOpenRetries { get; set; }\n" +
        "    public int? RetryDelayMs { get; set; }\n" +
        "    public JsonSerializerOptions? JsonSerializerOptions { get; set; }\n" +
        "    public bool EnableDiagnostics { get; set; } = true;\n" +
        "}\n");

    private static SourceText GetTemplate_Context(string ns, string methods) => SourceText.From($"namespace {ns};\n\n" +
        "using System.Data.Common;\nusing System.Diagnostics;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Microsoft.Data.SqlClient;\n\n" +
        "public partial class SpocRDbContext : ISpocRDbContext\n" +
        "{\n" +
        "    private readonly SpocRDbContextOptions _options;\n" +
        "    public SpocRDbContext(SpocRDbContextOptions options)\n" +
        "    {\n" +
        "        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new System.ArgumentException(\"ConnectionString must be provided\", nameof(options));\n" +
        "        _options = options;\n" +
    "        if (_options.CommandTimeout is null or <= 0) _options.CommandTimeout = 30;\n" +
        "    }\n" +
    "    public int CommandTimeout => _options.CommandTimeout ?? 30;\n" +
        "    public DbConnection OpenConnection()\n" +
        "    {\n" +
        "        var sw = _options.EnableDiagnostics ? Stopwatch.StartNew() : null;\n" +
        "        int attempt = 0; int max = _options.MaxOpenRetries.GetValueOrDefault(0); int delay = _options.RetryDelayMs.GetValueOrDefault(200);\n" +
        "        while (true) { try { var conn = new SqlConnection(_options.ConnectionString); conn.Open(); if (_options.EnableDiagnostics) { sw!.Stop(); System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnection latency=\" + sw.ElapsedMilliseconds + \"ms attempts=\" + (attempt+1)); } return conn; } catch (SqlException ex) when (attempt < max) { attempt++; if (_options.EnableDiagnostics) { System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnection retry \" + attempt + \"/\" + max + \" after error: \" + ex.Message); } Thread.Sleep(delay); continue; } catch (SqlException ex) { if (_options.EnableDiagnostics) { System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnection failed: \" + ex.Message); } throw; } }\n" +
        "    }\n" +
        "    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)\n" +
        "    {\n" +
        "        var sw = _options.EnableDiagnostics ? Stopwatch.StartNew() : null;\n" +
        "        int attempt = 0; int max = _options.MaxOpenRetries.GetValueOrDefault(0); int delay = _options.RetryDelayMs.GetValueOrDefault(200);\n" +
        "        while (true) { try { var conn = new SqlConnection(_options.ConnectionString); await conn.OpenAsync(cancellationToken).ConfigureAwait(false); if (_options.EnableDiagnostics) { sw!.Stop(); System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnectionAsync latency=\" + sw.ElapsedMilliseconds + \"ms attempts=\" + (attempt+1)); } return conn; } catch (SqlException ex) when (attempt < max) { attempt++; if (_options.EnableDiagnostics) { System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnectionAsync retry \" + attempt + \"/\" + max + \" after error: \" + ex.Message); } await Task.Delay(delay, cancellationToken).ConfigureAwait(false); continue; } catch (SqlException ex) { if (_options.EnableDiagnostics) { System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnectionAsync failed: \" + ex.Message); } throw; } }\n" +
        "    }\n" +
        "    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)\n" +
        "    {\n" +
        "        try { await using var conn = new SqlConnection(_options.ConnectionString); await conn.OpenAsync(cancellationToken).ConfigureAwait(false); return true; } catch { return false; }\n" +
        "    }\n" +
        (string.IsNullOrWhiteSpace(methods) ? string.Empty : methods) +
        "}\n");

    private static SourceText GetTemplate_Di(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System;\nusing Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.DependencyInjection;\n\n" +
        "public static class SpocRDbContextServiceCollectionExtensions\n" +
        "{\n" +
        "    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)\n" +
        "    {\n" +
        "        var explicitOptions = new SpocRDbContextOptions(); configure?.Invoke(explicitOptions);\n" +
    "        services.AddSingleton(provider => { var cfg = provider.GetService<IConfiguration>(); var name = explicitOptions.ConnectionStringName ?? \"DefaultConnection\"; var conn = explicitOptions.ConnectionString ?? cfg?.GetConnectionString(name); if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException($\"No connection string resolved for SpocRDbContext (options / IConfiguration:GetConnectionString('{name}')).\"); explicitOptions.ConnectionString = conn; if (explicitOptions.CommandTimeout is null or <= 0) explicitOptions.CommandTimeout = 30; if (explicitOptions.MaxOpenRetries is not null and < 0) throw new InvalidOperationException(\"MaxOpenRetries must be >= 0\"); if (explicitOptions.RetryDelayMs is not null and <= 0) throw new InvalidOperationException(\"RetryDelayMs must be > 0\"); return explicitOptions; });\n" +
        "        services.AddScoped<ISpocRDbContext>(sp => new SpocRDbContext(sp.GetRequiredService<SpocRDbContextOptions>()));\n" +
        "        return services;\n" +
        "    }\n" +
        "}\n");

    private static SourceText GetTemplate_Endpoints(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using Microsoft.AspNetCore.Builder;\nusing Microsoft.AspNetCore.Http;\nusing Microsoft.Extensions.DependencyInjection;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n" +
        "public static class SpocRDbContextEndpointRouteBuilderExtensions\n" +
        "{\n" +
        "    public static IEndpointRouteBuilder MapSpocRDbContextEndpoints(this IEndpointRouteBuilder endpoints)\n" +
        "    {\n" +
        "        endpoints.MapGet(\"/spocr/health/db\", async (ISpocRDbContext db, CancellationToken ct) => { var healthy = await db.HealthCheckAsync(ct).ConfigureAwait(false); return healthy ? Results.Ok(new { status = \"ok\" }) : Results.Problem(\"database unavailable\", statusCode: 503); });\n" +
        "        return endpoints;\n" +
        "    }\n" +
        "}\n");
    private async Task WriteAsync(string dir, string fileName, string source, bool isDryRun)
    {
        var path = Path.Combine(dir, fileName);
        await _outputService.WriteAsync(path, SourceText.From(source), isDryRun);
    }

    private static string NormalizeProcedurePart(string procPart)
    {
        if (string.IsNullOrWhiteSpace(procPart)) return "Procedure";
        var sb = new StringBuilder(procPart.Length);
        foreach (var ch in procPart)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        var candidate = sb.ToString();
        if (char.IsDigit(candidate[0])) candidate = "N" + candidate; // ensure valid identifier start
        return candidate;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Schema";
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
}
