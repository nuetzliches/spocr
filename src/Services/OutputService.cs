using System.IO;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Utils;

namespace SpocR.Services
{
    public class OutputService
    {
        private readonly FileManager<ConfigurationModel> _configFile;
        private readonly IReportService _reportService;

        public OutputService(FileManager<ConfigurationModel> configFile, IReportService reportService)
        {
            _configFile = configFile;
            _reportService = reportService;
        }

        public DirectoryInfo GetOutputRootDir()
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output"));
        }

        public void GenerateCodeBase(OutputModel output, bool dryrun)
        {
            var dir = GetOutputRootDir();

            var targetDir = DirectoryUtils.GetWorkingDirectory(output.DataContext.Path);
            CopyAllFiles(Path.Combine(dir.FullName, "DataContext"), targetDir, output.Namespace, dryrun);

            // var inputTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Inputs.Path);
            // CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Inputs"), inputTargetDir, output.Namespace, dryrun);

            var modelTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Models.Path);
            CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Models"), modelTargetDir, output.Namespace, dryrun);

            var paramsTargetDir = DirectoryUtils.GetWorkingDirectory(targetDir, output.DataContext.Params.Path);
            CopyAllFiles(Path.Combine(dir.FullName, "DataContext/Params"), paramsTargetDir, output.Namespace, dryrun);

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

        private void CopyFile(FileInfo file, string targetFileName, string nameSpace, bool isDryRun)
        {
            var fileContent = File.ReadAllText(file.FullName);
            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            if (_configFile.Config.Project.Role.Kind == ERoleKind.Lib)
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

            Write(targetFileName, sourceText, isDryRun);
        }

        public void Write(string targetFileName, SourceText sourceText, bool isDryRun)
        {
            var folderName = new DirectoryInfo(Path.GetDirectoryName(targetFileName)).Name;
            var fileName = Path.GetFileName(targetFileName);
            var fileAction = FileAction.Created;
            var outputFileText = sourceText.ToString();

            if (File.Exists(targetFileName))
            {
                var existingFileText = File.ReadAllText(targetFileName);
                var upToDate = string.Equals(existingFileText, outputFileText);

                fileAction = upToDate ? FileAction.UpToDate : FileAction.Modified;
            }

            if (!isDryRun && fileAction != FileAction.UpToDate)
                File.WriteAllText(targetFileName, outputFileText);

            _reportService.PrintFileActionMessage($"{folderName}/{fileName}", fileAction);
        }

        public void RemoveGeneratedFiles(string pathToDelete, bool dryRun)
        {
            if (Directory.Exists(pathToDelete))
            {
                _reportService.Warn($"DELETE: Generated spocr files");

                if (!dryRun)
                    Directory.Delete(pathToDelete, true);
            }
        }
    }
}