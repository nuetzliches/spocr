using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Internal.Models;
using SpocR.Utils;

namespace SpocR.Services
{
    public class OutputService
    {
        public DirectoryInfo GetSourceStructureRootDir()
        {
            return new DirectoryInfo(Path.Combine(DirectoryUtils.GetApplicationRoot(), "Internal", "SourceStructure"));
        }
        
        public IEnumerable<OutputModel> GetStructureModelListFromSource(DirectoryInfo rootDir = null, string parentPath = null, string nameSpace = null)
        {
            rootDir = rootDir ?? GetSourceStructureRootDir();
            foreach (var child in rootDir.GetDirectories())
            {
                var path = $"{parentPath ?? "."}/{child.Name}";
                yield return new OutputModel
                {
                    Namespace = nameSpace,
                    Name = child.Name,
                    Path = path,
                    Children = GetStructureModelListFromSource(child, path)
                };
            }
        }

        public void GenerateCodeBase(string nameSpace, bool dryrun)
        {

            var rootDir = GetSourceStructureRootDir();
            var baseFiles = rootDir.GetFiles("*.base.cs", SearchOption.AllDirectories);

            foreach (var file in baseFiles)
            {
                var fileContent = File.ReadAllText(file.FullName);

                var tree = CSharpSyntaxTree.ParseText(fileContent);
                var root = tree.GetCompilationUnitRoot();

                // Replace Namespace
                var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
                var name = SyntaxFactory.ParseName($"{nsNode.Name.ToString().Replace("Source.DataContext", nameSpace)}{Environment.NewLine}");
                root = root.ReplaceNode(nsNode, nsNode.WithName(name));

                if (dryrun)
                    return;

                var targetDir = Path.Combine(Directory.GetCurrentDirectory(), GetStructureNodeBySourcePath(file.DirectoryName).Path);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                File.WriteAllText(Path.Combine(targetDir, file.Name.Replace(".base.cs", ".cs")), root.GetText().ToString());
            }
        }

        private OutputModel GetStructureNodeBySourcePath(string path)
        {
            var info = new DirectoryInfo(path);
            var rootDir = GetSourceStructureRootDir();
            var relativePath = info.FullName.Replace(rootDir.FullName, "");

            var directories = relativePath
                                .Split(Path.DirectorySeparatorChar)
                                .Where(i => !string.IsNullOrWhiteSpace(i));

            // CHECK ME
            var strutureNode = directories.Any()
                ? _configFile.Config.Project.Output.SingleOrDefault(i => i.Name.Equals(directories.First()))
                : null;

            foreach (var dirName in directories.Skip(1))
            {
                strutureNode = strutureNode.Children.SingleOrDefault(i => i.Name.Equals(dirName));
            }

            return strutureNode;
        }
    }
}