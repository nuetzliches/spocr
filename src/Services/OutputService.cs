using System.IO;
using Microsoft.CodeAnalysis.CSharp;
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

        public OutputService(FileManager<ConfigurationModel> configFile)
        {
            _configFile = configFile;
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

        private void CopyFile(FileInfo file, string targetFileName, string nameSpace, bool dryrun)
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

            if (dryrun)
                return;

            var targetDir = Path.GetDirectoryName(targetFileName);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.WriteAllText(targetFileName, root.GetText().ToString());
        }

        public void RemoveGeneratedFiles(string pathToDelete, bool dryRun)
        {
            if (Directory.Exists(pathToDelete))
            {
                if (!dryRun)
                    Directory.Delete(pathToDelete, true);
            }
        }
    }
}