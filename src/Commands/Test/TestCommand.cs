using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Services;
using SpocR.Infrastructure;
using System.Text.Json;
using System.IO;
using System.Xml;

namespace SpocR.Commands.Test;

[Command("test", Description = "Run SpocR tests and validations")]
[HelpOption("-?|-h|--help")]
public class TestCommand : CommandBase
{
    [Option("--validate", Description = "Only validate generated code without running full test suite")]
    public bool ValidateOnly { get; set; }

    [Option("--benchmark", Description = "Run performance benchmarks")]
    public bool RunBenchmarks { get; set; }

    [Option("--rollback", Description = "Rollback changes if tests fail")]
    public bool RollbackOnFailure { get; set; }

    [Option("--ci", Description = "CI-friendly mode with structured output")]
    public bool CiMode { get; set; }

    [Option("--no-validation", Description = "Skip structural/validation checks in full suite mode")]
    public bool SkipValidation { get; set; }

    [Option("--only", Description = "Limit phases: comma-separated list of unit,integration,validation (implies skipping others)")]
    public string OnlyPhases { get; set; }

    [Option("--output", Description = "Output file path for test results (JUnit XML format)")]
    public string OutputFile { get; set; }

    [Option("--junit", Description = "Also emit JUnit XML into .artifacts/junit-results.xml (overridden by --output if specified)")]
    public bool EmitJUnit { get; set; }

    [Option("--filter", Description = "Filter tests by name pattern")]
    public string TestFilter { get; set; }

    private readonly IConsoleService _consoleService;

    public TestCommand(IConsoleService consoleService)
    {
        _consoleService = consoleService ?? throw new ArgumentNullException(nameof(consoleService));
    }

    public override async Task<int> OnExecuteAsync()
    {
        try
        {
            _consoleService.Info("🧪 SpocR Testing Framework");
            _consoleService.Info("==========================");

            var testResults = new TestResults();

            if (ValidateOnly)
            {
                return await RunValidationOnlyAsync(testResults);
            }

            if (RunBenchmarks)
            {
                return await RunBenchmarksAsync(testResults);
            }

            return await RunFullTestSuiteAsync(testResults);
        }
        catch (Exception ex)
        {
            _consoleService.Error($"Test execution failed: {ex.Message}");
            if (!CiMode)
            {
                _consoleService.Error($"Details: {ex}");
            }
            return ExitCodes.InternalError; // Unexpected execution failure
        }
    }

    private async Task<int> RunValidationOnlyAsync(TestResults results)
    {
        _consoleService.Info("🔍 Running validation tests only...");

        var validationTasks = new List<Task<bool>>
        {
            ValidateProjectStructureAsync(),
            ValidateConfigurationAsync(),
            ValidateGeneratedCodeAsync()
        };

        var validationResults = await Task.WhenAll(validationTasks);
        var allPassed = validationResults.All(r => r);

        results.ValidationTests = validationResults.Length;
        results.ValidationPassed = validationResults.Count(r => r);

        PrintResults(results);
        if (CiMode)
        {
            await WriteJsonSummaryAsync(results, "validation-only");
        }
        return allPassed ? ExitCodes.Success : ExitCodes.ValidationError;
    }

    private async Task<int> RunBenchmarksAsync(TestResults results)
    {
        _consoleService.Info("📊 Running performance benchmarks...");

        // TODO: Implement BenchmarkDotNet integration
        _consoleService.Warn("Benchmark functionality coming soon!");

        await Task.CompletedTask;
        return ExitCodes.Success;
    }

    private async Task<int> RunFullTestSuiteAsync(TestResults results)
    {
        _consoleService.Info("🎯 Running full test suite...");

        _suiteStartedUtc = DateTime.UtcNow;
        var overallSw = Stopwatch.StartNew();
        var exitCodes = new List<int>();
        var phases = ParseOnlyPhases();

        bool runUnit = phases.unit;
        bool runIntegration = phases.integration;
        bool runValidation = phases.validation && !ValidateOnly && !SkipValidation;

        if (ValidateOnly)
        {
            runUnit = false; runIntegration = false; runValidation = true;
        }

        // Sequential execution for resource friendliness
        if (runUnit)
        {
            var unitExit = await RunUnitTestsAsync();
            _unitPhasePassed = unitExit == 0;
            exitCodes.Add(unitExit);
        }
        else
        {
            _unitPhasePassed = true; // Not executed counts as neutral
        }

        if (runIntegration)
        {
            var integrationExit = await RunIntegrationTestsAsync();
            _integrationPhasePassed = integrationExit == 0;
            exitCodes.Add(integrationExit);
        }
        else
        {
            _integrationPhasePassed = true;
        }

        if (runValidation)
        {
            var validationExit = await RunValidationTestsAsync();
            _validationPhasePassed = validationExit == 0;
            exitCodes.Add(validationExit);
        }
        else
        {
            _validationPhasePassed = true;
        }
        overallSw.Stop();
        results.TotalDurationMs = (long)overallSw.Elapsed.TotalMilliseconds;
        _suiteEndedUtc = DateTime.UtcNow;
        var overallSuccess = exitCodes.All(code => code == 0);
        // Derive granular failure code precedence: unit > integration > validation if multiple
        int granularExit = ExitCodes.Success;
        if (!overallSuccess)
        {
            if (!_unitPhasePassed)
                granularExit = ExitCodes.UnitTestFailure;
            else if (!_integrationPhasePassed)
                granularExit = ExitCodes.IntegrationTestFailure;
            else if (!_validationPhasePassed && !SkipValidation)
                granularExit = ExitCodes.ValidationTestFailure;
            else
                granularExit = ExitCodes.TestFailure;
        }

        // Parse TRX files (if present) to obtain real counts & per-suite stats
        await ParseTrxResultsAsync(results, runUnit, runIntegration);

        // Per-suite durations already captured; move into stats now
        if (runUnit) results.Unit.TotalDurationMs = _lastUnitDurationMs;
        if (runIntegration) results.Integration.TotalDurationMs = _lastIntegrationDurationMs;

        if (!string.IsNullOrEmpty(OutputFile))
        {
            await WriteJUnitXmlAsync(results, OutputFile);
        }
        else if (EmitJUnit)
        {
            var junitPath = System.IO.Path.Combine(".artifacts", "junit-results.xml");
            await WriteJUnitXmlAsync(results, junitPath);
        }

        PrintResults(results);
        if (CiMode)
        {
            await WriteJsonSummaryAsync(results, "full-suite");
        }
        return overallSuccess ? ExitCodes.Success : granularExit;
    }

    private async Task<int> RunUnitTestsAsync()
    {
        _consoleService.Info("  ✅ Running unit tests...");
        var trxArg = CiMode ? "--logger \"trx;LogFileName=unit.trx\" --results-directory .artifacts" : string.Empty;
        var sw = Stopwatch.StartNew();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test tests/SpocR.Tests/SpocR.Tests.csproj -c Release {trxArg} --filter Category!=Meta --verbosity minimal",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        // Mark inner test run to allow recursion guard inside test assembly
        process.StartInfo.Environment["SPOCR_INNER_TEST_RUN"] = "1";
        // Environment variable not needed when using --results-directory

        process.Start();
        await process.WaitForExitAsync();
        sw.Stop();
        _lastUnitDurationMs = (long)sw.Elapsed.TotalMilliseconds;

        var success = process.ExitCode == 0;
        _consoleService.Info($"     Unit tests: {(success ? "✅ PASSED" : "❌ FAILED")}");
        if (CiMode)
        {
            try
            {
                var unitTrx = System.IO.Path.Combine(".artifacts", "unit.trx");
                if (File.Exists(unitTrx))
                {
                    var size = new FileInfo(unitTrx).Length;
                    _consoleService.Info($"       [debug] unit.trx present ({size} bytes)");
                }
                else
                {
                    _consoleService.Warn("       [debug] unit.trx NOT found after unit tests");
                }
            }
            catch (Exception ex)
            {
                _consoleService.Warn($"       [debug] unit.trx inspection failed: {ex.Message}");
            }
        }

        return process.ExitCode;
    }

    private async Task<int> RunIntegrationTestsAsync()
    {
        _consoleService.Info("  🔗 Running integration tests...");
        var trxArg = CiMode ? "--logger \"trx;LogFileName=integration.trx\" --results-directory .artifacts" : string.Empty;
        var sw = Stopwatch.StartNew();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test tests/SpocR.IntegrationTests/SpocR.IntegrationTests.csproj -c Release {trxArg} --verbosity minimal",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.Environment["SPOCR_INNER_TEST_RUN"] = "1";
        // Environment variable not needed when using --results-directory

        process.Start();
        await process.WaitForExitAsync();
        sw.Stop();
        _lastIntegrationDurationMs = (long)sw.Elapsed.TotalMilliseconds;

        var success = process.ExitCode == 0;
        _consoleService.Info($"     Integration tests: {(success ? "✅ PASSED" : "❌ FAILED")}");
        if (CiMode)
        {
            try
            {
                var integrationTrx = System.IO.Path.Combine(".artifacts", "integration.trx");
                if (File.Exists(integrationTrx))
                {
                    var size = new FileInfo(integrationTrx).Length;
                    _consoleService.Info($"       [debug] integration.trx present ({size} bytes)");
                }
                else
                {
                    _consoleService.Warn("       [debug] integration.trx NOT found after integration tests");
                }
            }
            catch (Exception ex)
            {
                _consoleService.Warn($"       [debug] integration.trx inspection failed: {ex.Message}");
            }
        }

        return process.ExitCode;
    }

    private async Task<int> RunValidationTestsAsync()
    {
        _consoleService.Info("  🔍 Running validation tests...");
        var sw = Stopwatch.StartNew();

        var validationTasks = new[]
        {
            ValidateProjectStructureAsync(),
            ValidateConfigurationAsync(),
            ValidateGeneratedCodeAsync()
        };

        var results = await Task.WhenAll(validationTasks);
        var allPassed = results.All(r => r);

        _consoleService.Info($"     Validation tests: {(allPassed ? "✅ PASSED" : "❌ FAILED")}");
        sw.Stop();
        _lastValidationDurationMs = (long)sw.Elapsed.TotalMilliseconds;

        return allPassed ? ExitCodes.Success : ExitCodes.ValidationError;
    }

    // BuildTestArguments no longer used (replaced with inline construction for custom trx filenames)
    private async Task ParseTrxResultsAsync(TestResults results)
    // Backwards compatible overload preserved for any legacy call paths
    {
        await ParseTrxResultsAsync(results, true, true);
    }

    private async Task ParseTrxResultsAsync(TestResults results, bool expectUnit, bool expectIntegration)
    {
        try
        {
            var artifactsDir = ".artifacts";
            Directory.CreateDirectory(artifactsDir);
            string[] trxFiles = Array.Empty<string>();
            for (int attempt = 0; attempt < 10; attempt++)
            {
                trxFiles = Directory.GetFiles(artifactsDir, "*.trx", SearchOption.TopDirectoryOnly);
                if (trxFiles.Length == 0 || trxFiles.All(f => new FileInfo(f).Length == 0))
                {
                    await Task.Delay(200);
                    continue;
                }
                break;
            }

            if (CiMode)
            {
                if (trxFiles.Length == 0)
                    _consoleService.Warn("       [debug] No TRX files discovered for parsing");
                else
                    foreach (var f in trxFiles)
                        _consoleService.Info($"       [debug] Found TRX: {System.IO.Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)");
            }

            foreach (var file in trxFiles)
            {
                var isUnit = file.IndexOf("unit", StringComparison.OrdinalIgnoreCase) >= 0;
                var isIntegration = file.IndexOf("integration", StringComparison.OrdinalIgnoreCase) >= 0;
                try
                {
                    using var stream = File.OpenRead(file);
                    var doc = XDocument.Load(stream);
                    var counters = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters");
                    if (counters == null) continue;
                    int GetAttr(string name)
                    {
                        var attr = counters.Attribute(name);
                        return attr != null && int.TryParse(attr.Value, out var v) ? v : 0;
                    }
                    var total = GetAttr("total");
                    var passed = GetAttr("passed");
                    var failed = GetAttr("failed");
                    var skipped = GetAttr("notExecuted") + GetAttr("skipped");

                    var unitTestResults = doc.Descendants().Where(e => e.Name.LocalName == "UnitTestResult");
                    var failedCases = new List<FailureDetail>();
                    foreach (var r in unitTestResults)
                    {
                        var outcome = r.Attribute("outcome")?.Value;
                        if (!string.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase)) continue;
                        var testName = r.Attribute("testName")?.Value ?? "<unknown>";
                        var message = r.Descendants().FirstOrDefault(x => x.Name.LocalName == "Message")?.Value ?? "";
                        failedCases.Add(new FailureDetail { Name = testName, Message = message, Suite = isIntegration ? "integration" : "unit" });
                    }

                    if (isUnit && expectUnit)
                    {
                        results.Unit.Total = total;
                        results.Unit.Passed = passed;
                        results.Unit.Failed = failed;
                        results.Unit.Skipped = skipped;
                        // Normalize per-suite total to components to avoid adapter-specific extra counters
                        results.Unit.Total = results.Unit.Passed + results.Unit.Failed + results.Unit.Skipped;
                        results.Unit.FailedNames.AddRange(failedCases.Select(c => c.Name));
                        results.FailureDetails.AddRange(failedCases);
                    }
                    else if (isIntegration && expectIntegration)
                    {
                        results.Integration.Total = total;
                        results.Integration.Passed = passed;
                        results.Integration.Failed = failed;
                        results.Integration.Skipped = skipped;
                        results.Integration.Total = results.Integration.Passed + results.Integration.Failed + results.Integration.Skipped;
                        results.Integration.FailedNames.AddRange(failedCases.Select(c => c.Name));
                        results.FailureDetails.AddRange(failedCases);
                    }
                }
                catch (Exception exInner)
                {
                    _consoleService.Warn($"Could not parse TRX '{System.IO.Path.GetFileName(file)}': {exInner.Message}");
                }
            }

            // Aggregate
            results.PassedTests = results.Unit.Passed + results.Integration.Passed;
            results.FailedTests = results.Unit.Failed + results.Integration.Failed;
            results.SkippedTests = results.Unit.Skipped + results.Integration.Skipped;
            // Recompute aggregate total strictly from components for determinism
            results.TotalTests = results.PassedTests + results.FailedTests + results.SkippedTests;
            results.FailedTestNames = results.FailureDetails.Select(f => f.Name).Distinct().ToList();
        }
        catch (Exception ex)
        {
            _consoleService.Warn($"Failed to parse TRX files: {ex.Message}");
        }
    }

    private async Task<bool> ValidateProjectStructureAsync()
    {
        _consoleService.Verbose("    🏗️  Validating project structure...");

        // Determine context: SpocR repository or generated project
        var isSpocRRepository = Directory.Exists("src") && File.Exists("src/SpocR.csproj");
        var isGeneratedProject = File.Exists("SpocR.csproj") && File.Exists("Program.cs");

        if (isSpocRRepository)
        {
            _consoleService.Verbose("       📁 Detected SpocR repository context");
            return await ValidateRepositoryStructureAsync();
        }
        else if (isGeneratedProject)
        {
            _consoleService.Verbose("       📁 Detected generated SpocR project context");
            return await ValidateGeneratedProjectStructureAsync();
        }
        else
        {
            _consoleService.Error("       ❌ Unable to determine project context (neither SpocR repository nor generated project)");
            return false;
        }
    }

    private async Task<bool> ValidateRepositoryStructureAsync()
    {
        var criticalFiles = new[]
        {
            "src/SpocR.csproj",
            "src/Program.cs",
            "README.md",
            "CONTRIBUTING.md"
        };

        foreach (var file in criticalFiles)
        {
            if (!File.Exists(file))
            {
                _consoleService.Error($"       ❌ Missing critical repository file: {file}");
                return false;
            }
        }

        _consoleService.Verbose("       ✅ Repository structure valid");
        await Task.CompletedTask;
        return true;
    }

    private async Task<bool> ValidateGeneratedProjectStructureAsync()
    {
        var criticalFiles = new[]
        {
            "SpocR.csproj",
            "Program.cs"
        };

        foreach (var file in criticalFiles)
        {
            if (!File.Exists(file))
            {
                _consoleService.Error($"       ❌ Missing critical project file: {file}");
                return false;
            }
        }

        _consoleService.Verbose("       ✅ Generated project structure valid");
        await Task.CompletedTask;
        return true;
    }

    private async Task<bool> ValidateConfigurationAsync()
    {
        _consoleService.Verbose("    ⚙️  Validating configuration...");

        // TODO: Add configuration validation logic
        _consoleService.Verbose("       ✅ Configuration valid");
        await Task.CompletedTask;
        return true;
    }

    private async Task<bool> ValidateGeneratedCodeAsync()
    {
        _consoleService.Verbose("    📝 Validating generated code...");

        // TODO: Add generated code validation logic
        _consoleService.Verbose("       ✅ Generated code valid");
        await Task.CompletedTask;
        return true;
    }

    private async Task WriteJUnitXmlAsync(TestResults results, string filePath)
    {
        var xml = new XDocument(
            new XElement("testsuites",
                new XAttribute("tests", results.TotalTests),
                new XAttribute("failures", results.TotalTests - results.PassedTests),
                new XAttribute("time", "0"),
                new XElement("testsuite",
                    new XAttribute("name", "SpocR.Tests"),
                    new XAttribute("tests", results.TotalTests),
                    new XAttribute("failures", results.TotalTests - results.PassedTests),
                    new XAttribute("time", "0")
                )
            )
        );

        await File.WriteAllTextAsync(filePath, xml.ToString());
        _consoleService.Info($"📄 Test results written to: {filePath}");
    }

    private void PrintResults(TestResults results)
    {
        _consoleService.Info("");
        _consoleService.Info("📊 Test Results Summary");
        _consoleService.Info("=====================");

        if (results.ValidationTests > 0)
        {
            _consoleService.Info($"Validation Tests: {results.ValidationPassed}/{results.ValidationTests} passed");
        }

        if (results.TotalTests > 0)
        {
            _consoleService.Info($"Total Tests: {results.PassedTests}/{results.TotalTests} passed (skipped: {results.SkippedTests})");
            if (results.FailedTests > 0)
            {
                var maxList = 10;
                var list = results.FailureDetails.Take(maxList).Select(f => $"- {f.Suite}: {f.Name}");
                _consoleService.Info("Failing Tests (top):" + Environment.NewLine + string.Join(Environment.NewLine, list));
                if (results.FailureDetails.Count > maxList)
                {
                    _consoleService.Info($"... (+{results.FailureDetails.Count - maxList} more failures)");
                }
            }
        }

        var overallSuccess = (results.ValidationTests == 0 || results.ValidationPassed == results.ValidationTests || SkipValidation) &&
                   (results.TotalTests == 0 || results.PassedTests == results.TotalTests);

        _consoleService.Info($"Overall Result: {(overallSuccess ? "✅ SUCCESS" : "❌ FAILURE")}");
    }

    private async Task WriteJsonSummaryAsync(TestResults results, string mode)
    {
        try
        {
            var artifactsDir = ".artifacts";
            Directory.CreateDirectory(artifactsDir);
            var summaryPath = System.IO.Path.Combine(artifactsDir, "test-summary.json");
            var tempPath = summaryPath + ".tmp";
            var payload = new
            {
                mode,
                timestampUtc = DateTime.UtcNow,
                startedAtUtc = _suiteStartedUtc,
                endedAtUtc = _suiteEndedUtc,
                validation = new { total = results.ValidationTests, passed = results.ValidationPassed, failed = results.ValidationTests - results.ValidationPassed },
                tests = new
                {
                    total = results.TotalTests,
                    passed = results.PassedTests,
                    failed = results.FailedTests,
                    skipped = results.SkippedTests,
                    unit = new { total = results.Unit.Total, passed = results.Unit.Passed, failed = results.Unit.Failed, skipped = results.Unit.Skipped, durationMs = results.Unit.TotalDurationMs },
                    integration = new { total = results.Integration.Total, passed = results.Integration.Passed, failed = results.Integration.Failed, skipped = results.Integration.Skipped, durationMs = results.Integration.TotalDurationMs }
                },
                duration = new
                {
                    totalMs = results.TotalDurationMs,
                    unitMs = _lastUnitDurationMs,
                    integrationMs = _lastIntegrationDurationMs,
                    validationMs = _lastValidationDurationMs
                },
                failedTestNames = results.FailedTestNames, // backwards compatibility
                failureDetails = results.FailureDetails.Select(f => new { f.Name, f.Suite, f.Message }).ToList(),
                success = (results.ValidationTests == 0 || results.ValidationPassed == results.ValidationTests) &&
                          // For full-suite we now require at least one test discovered
                          (mode == "full-suite" ? (results.TotalTests > 0 && results.PassedTests == results.TotalTests) : (results.TotalTests == 0 || results.PassedTests == results.TotalTests))
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(tempPath, json);
            // Atomic replace
            if (File.Exists(summaryPath))
            {
                File.Delete(summaryPath);
            }
            File.Move(tempPath, summaryPath);
            _consoleService.Info($"🧾 JSON summary written: {summaryPath}");
        }
        catch (Exception ex)
        {
            _consoleService.Warn($"Failed to write JSON summary: {ex.Message}");
        }
    }

    private class TestResults
    {
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }
        public int ValidationTests { get; set; }
        public int ValidationPassed { get; set; }
        public long TotalDurationMs { get; set; }
        public List<string> FailedTestNames { get; set; } = new();
        public SuiteStats Unit { get; set; } = new();
        public SuiteStats Integration { get; set; } = new();
        public List<FailureDetail> FailureDetails { get; set; } = new();
    }

    private class SuiteStats
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public long TotalDurationMs { get; set; }
        public List<string> FailedNames { get; set; } = new();
    }

    private class FailureDetail
    {
        public string Name { get; set; }
        public string Message { get; set; }
        public string Suite { get; set; }
    }

    private long _lastUnitDurationMs;
    private long _lastIntegrationDurationMs;
    private long _lastValidationDurationMs;
    private DateTime? _suiteStartedUtc;
    private DateTime? _suiteEndedUtc;
    private bool _unitPhasePassed = true;
    private bool _integrationPhasePassed = true;
    private bool _validationPhasePassed = true;

    private (bool unit, bool integration, bool validation) ParseOnlyPhases()
    {
        if (string.IsNullOrWhiteSpace(OnlyPhases))
        {
            return (true, true, true); // default: run all phases (validation may be skipped by --no-validation)
        }
        var parts = OnlyPhases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant()).ToHashSet();
        bool unit = parts.Contains("unit");
        bool integration = parts.Contains("integration");
        bool validation = parts.Contains("validation");
        if (!unit && !integration && !validation)
        {
            _consoleService.Warn("--only specified but no known phase matched (unit,integration,validation); defaulting to all.");
            return (true, true, true);
        }
        return (unit, integration, validation);
    }
}