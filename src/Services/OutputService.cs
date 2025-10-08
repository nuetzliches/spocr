using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Utils;

namespace SpocR.Services;

public class OutputService(
    FileManager<ConfigurationModel> configFile,
    IConsoleService consoleService
)
{
    public DirectoryInfo GetOutputRootDir()
    {
        // Determine desired versioned output folder name based on target framework.
        var cfg = configFile.Config; // may be null during early runs; guard below
        var targetFramework = cfg?.TargetFramework ?? Constants.DefaultTargetFramework.ToFrameworkString();
        string desiredFolder;

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            desiredFolder = "Output";
        }
        else if (targetFramework.StartsWith("net9"))
        {
            desiredFolder = "Output-v9-0";
        }
        else if (targetFramework.StartsWith("net8"))
        {
            desiredFolder = "Output-v9-0"; // net8 shares the v9 templates currently
        }
        else if (int.TryParse(targetFramework.Replace("net", "").Split('.')[0], out var versionNumber) && versionNumber >= 10)
        {
            // Modern Mode: dedicated template folder name (if real templates land later). Currently a placeholder / minimal fallback.
            desiredFolder = "Output-modern"; // reserved name for modern embedded templates
        }
        else if (int.TryParse(targetFramework.Replace("net", "").Split('.')[0], out versionNumber) && versionNumber >= 5)
        {
            desiredFolder = "Output-v5-0";
        }
        else
        {
            desiredFolder = "Output";
        }

        // Prefer template folders that live under src/ (repository layout) when present.
        var appRoot = DirectoryUtils.GetApplicationRoot();
        var candidateInSrc = Path.Combine(appRoot, "src", desiredFolder);
        var candidateAtRoot = Path.Combine(appRoot, desiredFolder);

        string resolvedPath = Directory.Exists(candidateInSrc) ? candidateInSrc : candidateAtRoot;

        // If the directory does not exist yet (e.g. fresh clone or new TF), create it so subsequent copy calls don't fail.
        if (!Directory.Exists(resolvedPath))
        {
            Directory.CreateDirectory(resolvedPath);
        }

        return new DirectoryInfo(resolvedPath);
    }

    public void GenerateCodeBase(OutputModel output, bool dryrun)
    {
        var dir = GetOutputRootDir();

        // Defensive fallbacks – in case normalization (older versions) did not populate DataContext structure
        output ??= new OutputModel();
        output.DataContext ??= new DataContextModel();
        output.DataContext.Path ??= "DataContext";
        output.DataContext.Inputs ??= new DataContextInputsModel { Path = output.DataContext.Inputs?.Path ?? "Inputs" };
        output.DataContext.Outputs ??= new DataContextOutputsModel { Path = output.DataContext.Outputs?.Path ?? "Outputs" };
        output.DataContext.Models ??= new DataContextModelsModel { Path = output.DataContext.Models?.Path ?? "Models" };
        output.DataContext.StoredProcedures ??= new DataContextStoredProceduresModel { Path = output.DataContext.StoredProcedures?.Path ?? "StoredProcedures" };
        output.DataContext.TableTypes ??= new DataContextTableTypesModel { Path = output.DataContext.TableTypes?.Path ?? "TableTypes" };

        if (string.IsNullOrWhiteSpace(output.Namespace))
        {
            consoleService.Warn("[normalize-late] Output.Namespace was empty entering GenerateCodeBase – applying fallback 'SpocR.Generated'.");
            output.Namespace = "SpocR.Generated";
        }

        var targetDir = DirectoryUtils.GetWorkingDirectory(output.DataContext.Path);
        consoleService.Verbose($"[codebase] targetDir={targetDir} inputs={output.DataContext.Inputs.Path} outputs={output.DataContext.Outputs.Path} models={output.DataContext.Models.Path} sps={output.DataContext.StoredProcedures.Path} udtts={output.DataContext.TableTypes.Path}");
        // Ensure the versioned DataContext template source exists; if not, emit a warning instead of throwing.
        var templateDataContextDir = Path.Combine(dir.FullName, "DataContext");
        if (!Directory.Exists(templateDataContextDir))
        {
            consoleService.Warn($"Template source directory '{templateDataContextDir}' not found. Skipping base code copy.");
            return; // Without templates we cannot proceed copying base files.
        }
        CopyAllFiles(templateDataContextDir, targetDir, output.Namespace, dryrun);

        // var inputTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Inputs.Path);
        // CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Inputs"), inputTargetDir, output.Namespace, dryrun);

        var outputsTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Outputs.Path);
        CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Outputs"), outputsTargetDir, output.Namespace, dryrun);

        var modelTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Models.Path);
        CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Models"), modelTargetDir, output.Namespace, dryrun);

        var paramsTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.TableTypes.Path);
        CopyAllFiles(Path.Combine(dir.FullName, "DataContext/TableTypes"), paramsTargetDir, output.Namespace, dryrun);

        var spTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.StoredProcedures.Path);
        CopyAllFiles(Path.Combine(dir.FullName, "DataContext/StoredProcedures"), spTargetDir, output.Namespace, dryrun);
    }

    private void CopyAllFiles(string sourceDir, string targetDir, string nameSpace, bool dryrun)
    {
        var baseFiles = new DirectoryInfo(sourceDir).GetFiles("*.base.cs", SearchOption.TopDirectoryOnly);
        foreach (var file in baseFiles)
        {
            CopyFile(file, Path.Combine(targetDir, file.Name.Replace(".base", "")), nameSpace, dryrun);
        }
    }

    private async void CopyFile(FileInfo file, string targetFileName, string nameSpace, bool isDryRun)
    {
        var fileContent = await File.ReadAllTextAsync(file.FullName);

        // replace custom DefaultConnection identifier
        // Modern Mode (net10+): ignore configured RuntimeConnectionStringIdentifier (deprecated) – always map to static "DefaultConnection"
        string runtimeConnectionStringIdentifier;
        var cfgLocal = configFile.Config;
        if (IsModern(cfgLocal?.TargetFramework))
        {
            if (!string.IsNullOrWhiteSpace(cfgLocal?.Project?.DataBase?.RuntimeConnectionStringIdentifier))
            {
                consoleService.Verbose("[modern] Ignoring configured RuntimeConnectionStringIdentifier (deprecated in modern mode). Use service options to override at runtime.");
            }
            runtimeConnectionStringIdentifier = "DefaultConnection";
        }
        else
        {
            runtimeConnectionStringIdentifier = cfgLocal?.Project?.DataBase?.RuntimeConnectionStringIdentifier ?? "DefaultConnection";
        }
        fileContent = fileContent.Replace(@"<spocr>DefaultConnection</spocr>", runtimeConnectionStringIdentifier);

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        if (string.IsNullOrWhiteSpace(nameSpace))
        {
            throw new System.InvalidOperationException("OutputService: Provided namespace is empty – ensure configuration Project.Output.Namespace is set before generation.");
        }

        // Normalize to avoid double dots
        string Normalize(string ns) => ns.Replace("..", ".").Trim('.');
        nameSpace = Normalize(nameSpace);

        // Avoid double "DataContext" when caller already provides a namespace ending with ".DataContext"
        var endsWithDataContext = nameSpace.EndsWith(".DataContext", System.StringComparison.OrdinalIgnoreCase);
        if (configFile.Config.Project.Role.Kind == RoleKindEnum.Lib || endsWithDataContext)
        {
            // Replace the exact Source.DataContext root with the provided namespace
            root = root.ReplaceUsings(u => u.Replace("Source.DataContext", nameSpace));
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", nameSpace));
        }
        else
        {
            // Generic replacement keeping segment structure (e.g., Source.DataContext -> <ns>.DataContext)
            root = root.ReplaceUsings(u => u.Replace("Source.", nameSpace + "."));
            root = root.ReplaceNamespace(ns => ns.Replace("Source.", nameSpace + "."));
        }

        var targetDir = Path.GetDirectoryName(targetFileName);
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var sourceText = root.GetText();

        await WriteAsync(targetFileName, sourceText, isDryRun);
    }

    private static bool IsModern(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net")) return false;
        var core = tfm.Substring(3).Split('.')[0];
        return int.TryParse(core, out var major) && major >= 10;
    }

    public async Task WriteAsync(string targetFileName, SourceText sourceText, bool isDryRun)
    {
        var folderName = new DirectoryInfo(Path.GetDirectoryName(targetFileName)).Name;
        var fileName = Path.GetFileName(targetFileName);
        var fileAction = FileActionEnum.Created;
        var outputFileText = sourceText.ToString();

        // Normalize to file-scoped namespaces and use records for DataContext types
        try
        {
            // Convert classes to records ONLY for Inputs and TableTypes (keep Models/Outputs as classes)
            var recordFolders = new[] { "Inputs", "TableTypes" };
            var parentFolder = new DirectoryInfo(Path.GetDirectoryName(targetFileName)).Parent?.Name;
            var isRecordFolder = recordFolders.Any(f => string.Equals(folderName, f, StringComparison.OrdinalIgnoreCase))
                                 || recordFolders.Any(f => string.Equals(parentFolder, f, StringComparison.OrdinalIgnoreCase));
            if (isRecordFolder)
            {
                // partial classes -> partial records
                outputFileText = System.Text.RegularExpressions.Regex.Replace(outputFileText, @"\bpublic\s+partial\s+class\b", "public partial record");
                // simple classes -> records
                outputFileText = System.Text.RegularExpressions.Regex.Replace(outputFileText, @"\bpublic\s+class\b", "public record");
            }
        }
        catch { }

        // Inject XML auto-generated header (similar style to DataContext.Models) if not already present.
        // We avoid duplicating when file already contains the marker 'Auto-generated by SpocR.'
        const string headerMarker = "Auto-generated by SpocR.";
        if (!outputFileText.Contains(headerMarker))
        {
            var timestamp = System.DateTime.UtcNow.ToString("u").Replace(' ', ' '); // keep 'Z' style via ToString("u") ends with 'Z'
            var header =
                "/// <summary>Auto-generated by SpocR. DO NOT EDIT. Changes will be overwritten on rebuild.</summary>\r\n" +
                $"/// <remarks>Generated at {timestamp}</remarks>\r\n";

            // If file starts with using directives, place header before them, else at top.
            // Preserve BOM if present
            if (outputFileText.StartsWith("using "))
            {
                outputFileText = header + outputFileText;
            }
            else
            {
                outputFileText = header + outputFileText;
            }
        }

        // Special cleanup for TableTypes: ensure positional record declaration stands alone (trim any leftover stub body)
        try
        {
            var isTableTypesPath = targetFileName.IndexOf(Path.DirectorySeparatorChar + "TableTypes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
            if (isTableTypesPath && outputFileText.Contains("public record ") && outputFileText.Contains("ITableType"))
            {
                var recIdx = outputFileText.IndexOf("public record ", StringComparison.Ordinal);
                var endIdx = outputFileText.IndexOf(") : ITableType;", recIdx >= 0 ? recIdx : 0, StringComparison.Ordinal);
                if (recIdx >= 0 && endIdx > recIdx)
                {
                    // Keep everything up to and including the record declaration line
                    var headerPrefixEnd = outputFileText.LastIndexOf("namespace ", recIdx, StringComparison.Ordinal);
                    if (headerPrefixEnd < 0) headerPrefixEnd = 0;
                    var prefix = outputFileText.Substring(0, endIdx + ") : ITableType;".Length);
                    outputFileText = prefix + Environment.NewLine;
                }
            }
        }
        catch { }

        if (File.Exists(targetFileName))
        {
            var existingFileText = await File.ReadAllTextAsync(targetFileName);
            var upToDate = string.Equals(existingFileText, outputFileText);

            fileAction = upToDate ? FileActionEnum.UpToDate : FileActionEnum.Modified;
        }

        if (!isDryRun && fileAction != FileActionEnum.UpToDate)
            await File.WriteAllTextAsync(targetFileName, outputFileText);

        consoleService.PrintFileActionMessage($"{folderName}/{fileName}", fileAction);
    }

    public void RemoveGeneratedFiles(string pathToDelete, bool dryRun)
    {
        if (Directory.Exists(pathToDelete))
        {
            consoleService.Warn($"DELETE: Generated spocr files");

            if (!dryRun)
                Directory.Delete(pathToDelete, true);
        }
    }
}
