using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;
using SpocR.SpocRVNext.Engine;
using SpocRVNext.Configuration; // for EnvConfiguration & NamespaceResolver

namespace SpocR.SpocRVNext.Generators;

/// <summary>
/// Generates the DbContext related artifacts from templates (interface, context, options, DI extension, endpoints).
/// Generation is enabled automatically when SPOCR_GENERATOR_MODE is 'dual' or 'next'; skipped in 'legacy' mode.
/// </summary>
public class DbContextGenerator
{
    private readonly FileManager<ConfigurationModel> _configFile;
    private readonly OutputService _outputService;
    private readonly IConsoleService _console;
    private readonly ITemplateRenderer _renderer;
    private readonly ITemplateLoader? _loader;
    private readonly IGeneratorModeProvider _modeProvider;

    public DbContextGenerator(FileManager<ConfigurationModel> configFile, OutputService outputService, IConsoleService console, ITemplateRenderer renderer, IGeneratorModeProvider modeProvider, ITemplateLoader? loader = null)
    {
        _configFile = configFile;
        _outputService = outputService;
        _console = console;
        _renderer = renderer;
        _loader = loader;
        _modeProvider = modeProvider;
    }
    private bool IsEnabled() => _modeProvider.Mode is "dual" or "next";

    public Task GenerateAsync(bool isDryRun) => GenerateInternalAsync(isDryRun);

    private async Task GenerateInternalAsync(bool isDryRun)
    {
        if (!IsEnabled())
        {
            _console.Verbose("[dbctx] Skipped (generator mode is 'legacy')");
            return;
        }

        // Resolve base namespace via configuration / project structure (unified approach)
        var explicitNs = _configFile.Config.Project.Output.Namespace?.Trim();
        string baseNs;
        if (!string.IsNullOrWhiteSpace(explicitNs))
        {
            baseNs = explicitNs!;
        }
        else
        {
            try
            {
                // Use NamespaceResolver fallback (shared heuristic) – prevents silent skip in tests.
                var envCfg = EnvConfiguration.Load();
                var resolver = new NamespaceResolver(envCfg, msg => _console.Warn("[dbctx] ns-resolver: " + msg));
                baseNs = resolver.Resolve(Directory.GetCurrentDirectory());
                _console.Verbose($"[dbctx] Derived namespace '{baseNs}' (no explicit Project.Output.Namespace)");
            }
            catch (Exception ex)
            {
                _console.Warn("[dbctx] Missing Project.Output.Namespace – derivation failed: " + ex.Message);
                return;
            }
        }

        // Determine base directory (parent of DataContext) or override via ENV
        var dcPath = _configFile.Config.Project.Output.DataContext.Path;
        var dcDir = DirectoryUtils.GetWorkingDirectory(dcPath);
        var rootDir = dcDir;
        if (!string.IsNullOrWhiteSpace(dcPath) && dcPath.Trim('.', ' ', '/', '\\').Length > 0)
        {
            var parent = Directory.GetParent(dcDir);
            if (parent != null) rootDir = parent.FullName;
        }
        var spocrDir = Path.Combine(rootDir, "SpocR");
        try
        {
            // Zusatzdiagnose: aktuelle CWD und Raw WorkingDirectory ohne dcPath
            var rawWorking = DirectoryUtils.GetWorkingDirectory();
            _console.Verbose($"[dbctx] mode={_modeProvider.Mode} dcPath='{dcPath ?? "<null>"}' cwd={Directory.GetCurrentDirectory()} rawWorking={rawWorking} rootDir={rootDir} spocrDir={spocrDir}");
        }
        catch (Exception ex)
        {
            _console.Verbose($"[dbctx] diag-error: {ex.Message}");
        }
        if (!Directory.Exists(spocrDir) && !isDryRun) Directory.CreateDirectory(spocrDir);

        // Append .SpocR suffix only if not already present.
        var finalNs = baseNs.EndsWith(".SpocR", StringComparison.Ordinal) ? baseNs : baseNs + ".SpocR";
        var model = new { Namespace = finalNs };

        // Generate all artifacts
        await WriteAsync(spocrDir, "ISpocRDbContext.cs", Render("ISpocRDbContext", GetTemplate_Interface(finalNs), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextOptions.cs", Render("SpocRDbContextOptions", GetTemplate_Options(finalNs), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContext.cs", Render("SpocRDbContext", GetTemplate_Context(finalNs), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextServiceCollectionExtensions.cs", Render("SpocRDbContextServiceCollectionExtensions", GetTemplate_Di(finalNs), model), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextEndpoints.cs", Render("SpocRDbContextEndpoints", GetTemplate_Endpoints(finalNs), model), isDryRun);
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

    private static SourceText GetTemplate_Interface(string ns) => SourceText.From($"" +
        "/// <summary>Generated interface for the database context abstraction.</summary>\n" +
        $"namespace {ns};\n\n" +
        "using System.Data.Common;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n" +
        "public interface ISpocRDbContext\n" +
        "{\n" +
        "    DbConnection OpenConnection();\n" +
        "    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);\n" +
        "    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);\n" +
        "    int CommandTimeoutSeconds { get; }\n" +
        "}\n");

    private static SourceText GetTemplate_Options(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System.Text.Json;\n\n" +
        "public sealed class SpocRDbContextOptions\n{\n" +
        "    public string? ConnectionString { get; set; }\n" +
        "    public string? ConnectionStringName { get; set; }\n" +
        "    public int? CommandTimeoutSeconds { get; set; }\n" +
        "    public int? MaxOpenRetries { get; set; }\n" +
        "    public int? RetryDelayMs { get; set; }\n" +
        "    public bool ValidateOnBuild { get; set; } = false;\n" +
        "    public JsonSerializerOptions? JsonSerializerOptions { get; set; }\n" +
        "    public bool EnableDiagnostics { get; set; } = true;\n" +
        "}\n");

    private static SourceText GetTemplate_Context(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System.Data.Common;\nusing System.Diagnostics;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Microsoft.Data.SqlClient;\n\n" +
        "public partial class SpocRDbContext : ISpocRDbContext\n" +
        "{\n" +
        "    private readonly SpocRDbContextOptions _options;\n" +
        "    public SpocRDbContext(SpocRDbContextOptions options)\n" +
        "    {\n" +
        "        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new System.ArgumentException(\"ConnectionString must be provided\", nameof(options));\n" +
        "        _options = options;\n" +
        "        if (_options.CommandTimeoutSeconds is null or <= 0) _options.CommandTimeoutSeconds = 30;\n" +
        "    }\n" +
        "    public int CommandTimeoutSeconds => _options.CommandTimeoutSeconds ?? 30;\n" +
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
        "}\n");

    private static SourceText GetTemplate_Di(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System;\nusing Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.DependencyInjection;\n\n" +
        "public static class SpocRDbContextServiceCollectionExtensions\n" +
        "{\n" +
        "    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)\n" +
        "    {\n" +
        "        var explicitOptions = new SpocRDbContextOptions(); configure?.Invoke(explicitOptions);\n" +
        "        services.AddSingleton(provider => { var cfg = provider.GetService<IConfiguration>(); var name = explicitOptions.ConnectionStringName ?? \"DefaultConnection\"; var conn = explicitOptions.ConnectionString ?? cfg?.GetConnectionString(name) ?? Environment.GetEnvironmentVariable(\"SPOCR_DB_DEFAULT\"); if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException(\"No connection string resolved for SpocRDbContext (options, \" + name + \", or SPOCR_DB_DEFAULT).\"); explicitOptions.ConnectionString = conn; if (explicitOptions.CommandTimeoutSeconds is null or <= 0) explicitOptions.CommandTimeoutSeconds = 30; if (explicitOptions.MaxOpenRetries is not null and < 0) throw new InvalidOperationException(\"MaxOpenRetries must be >= 0\"); if (explicitOptions.RetryDelayMs is not null and <= 0) throw new InvalidOperationException(\"RetryDelayMs must be > 0\"); if (explicitOptions.ValidateOnBuild) { try { using var probe = new Microsoft.Data.SqlClient.SqlConnection(conn); probe.Open(); } catch (Exception ex) { throw new InvalidOperationException(\"SpocRDbContext ValidateOnBuild failed to open connection\", ex); } } return explicitOptions; });\n" +
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
}
