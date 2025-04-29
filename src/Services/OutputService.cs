using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
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
        if (string.IsNullOrEmpty(configFile.Config.TargetFramework))
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output"));
        }

        var targetFramework = configFile.Config.TargetFramework;
        _ = int.TryParse(targetFramework?.Replace("net", "")[0].ToString(), out var versionNumber);

        if (targetFramework.StartsWith("net8") || targetFramework.StartsWith("net9"))
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output-v9-0"));
        }
        else if (versionNumber >= 5)
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output-v5-0"));
        }
        else
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output"));
        }
    }

    public void GenerateCodeBase(OutputModel output, bool dryrun)
    {
        var dir = GetOutputRootDir();

        var targetDir = DirectoryUtils.GetWorkingDirectory(output.DataContext.Path);
        CopyAllFiles(Path.Combine(dir.FullName, "DataContext"), targetDir, output.Namespace, dryrun);

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
        var runtimeConnectionStringIdentifier = configFile.Config.Project.DataBase.RuntimeConnectionStringIdentifier ?? "DefaultConnection";
        fileContent = fileContent.Replace(@"<spocr>DefaultConnection</spocr>", runtimeConnectionStringIdentifier);

        var tree = CSharpSyntaxTree.ParseText(fileContent);
        var root = tree.GetCompilationUnitRoot();

        if (configFile.Config.Project.Role.Kind == RoleKindEnum.Lib)
        {
            root = root.ReplaceUsings(u => u.Replace("Source.DataContext", $"{nameSpace}"));
            root = root.ReplaceNamespace(ns => ns.Replace("Source.DataContext", nameSpace));
        }
        else
        {
            root = root.ReplaceUsings(u => u.Replace("Source.", $"{nameSpace}."));
            root = root.ReplaceNamespace(ns => ns.Replace("Source.", $"{nameSpace}."));
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
