using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.SpocRVNext.Generators;

/// <summary>
/// Generates the DbContext related artifacts from templates (interface, context, options, DI extension).
/// Generation is gated behind env flag SPOCR_GENERATE_DBCTX=1 (defaults off to avoid churn).
/// </summary>
public class DbContextGenerator
{
    private readonly FileManager<ConfigurationModel> _configFile;
    private readonly OutputService _outputService;
    private readonly IConsoleService _console;

    public DbContextGenerator(FileManager<ConfigurationModel> configFile, OutputService outputService, IConsoleService console)
    {
        _configFile = configFile;
        _outputService = outputService;
        _console = console;
    }

    private static bool IsEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SPOCR_GENERATE_DBCTX"), "1", StringComparison.OrdinalIgnoreCase);

    public Task GenerateAsync(bool isDryRun) => GenerateInternalAsync(isDryRun);

    private async Task GenerateInternalAsync(bool isDryRun)
    {
        if (!IsEnabled())
        {
            _console.Verbose("[dbctx] Skipped (SPOCR_GENERATE_DBCTX != 1)");
            return;
        }

        var nsRoot = _configFile.Config.Project.Output.Namespace?.Trim();
        if (string.IsNullOrWhiteSpace(nsRoot))
        {
            _console.Warn("[dbctx] Missing Project.Output.Namespace – aborting DbContext generation.");
            return;
        }

        // Decide target root path (DataContext root or flattened)
        var dataContextPath = _configFile.Config.Project.Output.DataContext.Path;
        var baseDir = DirectoryUtils.GetWorkingDirectory(dataContextPath);
        var spocrDir = Path.Combine(baseDir, "SpocR");
        if (!Directory.Exists(spocrDir) && !isDryRun) Directory.CreateDirectory(spocrDir);

        string ResolveNamespace()
        {
            var flatten = string.IsNullOrWhiteSpace(dataContextPath) || dataContextPath.Trim().TrimEnd('/', '\\') == ".";
            return flatten ? nsRoot + ".SpocR" : nsRoot + ".DataContext.SpocR";
        }

        var finalNs = ResolveNamespace();

        await WriteAsync(spocrDir, "ISpocRDbContext.cs", GetTemplate_Interface(finalNs), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextOptions.cs", GetTemplate_Options(finalNs), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContext.cs", GetTemplate_Context(finalNs), isDryRun);
        await WriteAsync(spocrDir, "SpocRDbContextServiceCollectionExtensions.cs", GetTemplate_Di(finalNs), isDryRun);
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
        "    public int? CommandTimeoutSeconds { get; set; }\n" +
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
        "        try { var conn = new SqlConnection(_options.ConnectionString); conn.Open(); if (_options.EnableDiagnostics) { sw!.Stop(); System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnection latency=\" + sw.ElapsedMilliseconds + \"ms\"); } return conn; }\n" +
        "        catch (SqlException ex) { if (_options.EnableDiagnostics) System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnection failed: \" + ex.Message); throw; }\n" +
        "    }\n" +
        "    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)\n" +
        "    {\n" +
        "        var sw = _options.EnableDiagnostics ? Stopwatch.StartNew() : null;\n" +
        "        try { var conn = new SqlConnection(_options.ConnectionString); await conn.OpenAsync(cancellationToken).ConfigureAwait(false); if (_options.EnableDiagnostics) { sw!.Stop(); System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnectionAsync latency=\" + sw.ElapsedMilliseconds + \"ms\"); } return conn; }\n" +
        "        catch (SqlException ex) { if (_options.EnableDiagnostics) System.Diagnostics.Debug.WriteLine(\"[SpocRDbContext] OpenConnectionAsync failed: \" + ex.Message); throw; }\n" +
        "    }\n" +
        "    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)\n" +
        "    {\n" +
        "        try { await using var conn = new SqlConnection(_options.ConnectionString); await conn.OpenAsync(cancellationToken).ConfigureAwait(false); return true; } catch { return false; }\n" +
        "    }\n" +
        "}\n");

    private static SourceText GetTemplate_Di(string ns) => SourceText.From($"namespace {ns};\n\n" +
        "using System;\nusing Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.DependencyInjection;\n\n" +
        "public static class SpocRDbContextServiceCollectionExtensions\n{\n" +
        "    public static IServiceCollection AddSpocRDbContext(this IServiceCollection services, Action<SpocRDbContextOptions>? configure = null)\n" +
        "    {\n" +
        "        var explicitOptions = new SpocRDbContextOptions(); configure?.Invoke(explicitOptions);\n" +
        "        services.AddSingleton(provider => { var cfg = provider.GetService<IConfiguration>(); var conn = explicitOptions.ConnectionString ?? cfg?.GetConnectionString(\"DefaultConnection\") ?? Environment.GetEnvironmentVariable(\"SPOCR_DB_DEFAULT\"); if (string.IsNullOrWhiteSpace(conn)) throw new InvalidOperationException(\"No connection string resolved for SpocRDbContext (options, DefaultConnection, or SPOCR_DB_DEFAULT).\"); explicitOptions.ConnectionString = conn; if (explicitOptions.CommandTimeoutSeconds is null or <= 0) explicitOptions.CommandTimeoutSeconds = 30; return explicitOptions; });\n" +
        "        services.AddScoped<ISpocRDbContext>(sp => new SpocRDbContext(sp.GetRequiredService<SpocRDbContextOptions>()));\n" +
        "        return services;\n" +
        "    }\n" +
        "}\n");

    private async Task WriteAsync(string dir, string fileName, SourceText source, bool isDryRun)
    {
        var path = Path.Combine(dir, fileName);
        await _outputService.WriteAsync(path, source, isDryRun);
    }
}
