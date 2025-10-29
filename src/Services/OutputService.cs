using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Infrastructure;
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
        var targetFramework = configFile.Config.TargetFramework;
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
        else if (int.TryParse(targetFramework.Replace("net", "").Split('.')[0], out var versionNumber) && versionNumber >= 5)
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

        var targetDir = DirectoryUtils.GetWorkingDirectory(output.DataContext.Path);
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
            // Certain *.base.cs files are owned by dedicated generators (e.g. Outputs.base.cs / CrudResult.base.cs)
            // which later consolidate into their final single-file form (Outputs.cs / CrudResult.cs).
            // Copying them here would introduce a double write (template copy + Roslyn/generated rewrite)
            // causing persistent "modified" noise due to formatting / trivia and timestamp differences.
            // To keep builds deterministic, we skip those here and let the specialized generators handle them.
            var fileName = file.Name;
            if (fileName.Equals("Outputs.base.cs", System.StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("CrudResult.base.cs", System.StringComparison.OrdinalIgnoreCase))
            {
                continue; // skip – specialized generator will produce the consolidated file
            }

            CopyFile(file, Path.Combine(targetDir, file.Name.Replace(".base", "")), nameSpace, dryrun);
        }
    }

    private async void CopyFile(FileInfo file, string targetFileName, string nameSpace, bool isDryRun)
    {
        var fileContent = await File.ReadAllTextAsync(file.FullName);

        // replace custom DefaultConnection identifier
        var runtimeConnectionStringIdentifier = configFile.Config.Project.DataBase.RuntimeConnectionStringIdentifier ?? "DefaultConnection";
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

        if (configFile.Config.Project.Role.Kind == RoleKindEnum.Lib)
        {
            root = root.ReplaceUsings(u => u.Replace("Source.DataContext", nameSpace));
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", nameSpace));
        }
        else
        {
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

    public async Task WriteAsync(string targetFileName, SourceText sourceText, bool isDryRun)
    {
        var folderName = new DirectoryInfo(Path.GetDirectoryName(targetFileName)).Name;
        var fileName = Path.GetFileName(targetFileName);
        var fileAction = FileActionEnum.Created;
        var outputFileText = sourceText.ToString();

        // Legacy XML auto-generated header removed to reduce diff churn and align with vNext minimalist output style.

        bool exists = File.Exists(targetFileName);
        if (exists)
        {
            var existingFileText = await File.ReadAllTextAsync(targetFileName);

            // Normalize volatile timestamp remark line so only semantic changes produce a diff.
            static string NormalizeForComparison(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                // Remove legacy volatile <remarks> lines (no longer emitted) for backward compatibility when comparing existing files.
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"^.*///\s<remarks>Generated at .*?</remarks>.*$",
                    string.Empty,
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                // Normalize line endings to \n
                text = text.Replace("\r\n", "\n");
                // Trim trailing whitespace per line
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = lines[i].TrimEnd();
                }
                text = string.Join("\n", lines);
                if (!text.EndsWith("\n")) text += "\n";
                return text;
            }

            var normExisting = NormalizeForComparison(existingFileText);
            var normNew = NormalizeForComparison(outputFileText);
            var upToDate = string.Equals(normExisting, normNew, System.StringComparison.Ordinal);
            fileAction = upToDate ? FileActionEnum.UpToDate : FileActionEnum.Modified;
        }
        // Schreibstrategie: Bei Modified ersetzen, bei UpToDate nicht anfassen.
        if (!isDryRun && fileAction != FileActionEnum.UpToDate)
        {
            await File.WriteAllTextAsync(targetFileName, outputFileText);
        }

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
