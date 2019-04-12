using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Extensions;
using SpocR.Models;
using SpocR.Utils;

namespace SpocR.Services
{
    public class OutputService
    {
        public DirectoryInfo GetOutputRootDir()
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Output"));
        }

        // Static implementation to copy files
        public void GenerateCodeBase(OutputModel output, bool dryrun)
        {
            var dir = GetOutputRootDir();

            var targetDir = Path.Combine(Directory.GetCurrentDirectory(), output.DataContext.Path);
            CopyAllFileFromTo(Path.Combine(dir.FullName, "DataContext"), targetDir, output.Namespace, dryrun);

            var modelTargetDir = Path.Combine(Directory.GetCurrentDirectory(), targetDir, output.DataContext.Models.Path);
            CopyAllFileFromTo(Path.Combine(dir.FullName, "DataContext/Models"), modelTargetDir, output.Namespace, dryrun);

            var paramsTargetDir = Path.Combine(Directory.GetCurrentDirectory(), targetDir, output.DataContext.Params.Path);
            CopyAllFileFromTo(Path.Combine(dir.FullName, "DataContext/Params"), paramsTargetDir, output.Namespace, dryrun);

            var spTargetDir = Path.Combine(Directory.GetCurrentDirectory(), targetDir, output.DataContext.StoredProcedures.Path);
            CopyAllFileFromTo(Path.Combine(dir.FullName, "DataContext/StoredProcedures"), spTargetDir, output.Namespace, dryrun);
        }

        private void CopyAllFileFromTo(string sourceDir, string targetDir, string nameSpace, bool dryrun)
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

            root = root.ReplaceNamespace(ns => ns.Replace("Source", nameSpace));

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