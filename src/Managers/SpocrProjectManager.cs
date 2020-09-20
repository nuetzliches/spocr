using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands;
using SpocR.Commands.Project;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers
{
    public class SpocrProjectManager
    {
        private readonly FileManager<GlobalConfigurationModel> _globalConfigFile;
        private readonly IReportService _reportService;

        public SpocrProjectManager(
            FileManager<GlobalConfigurationModel> globalConfigFile,
            IReportService reportService
        )
        {
            _globalConfigFile = globalConfigFile;
            _reportService = reportService;
        }

        public ExecuteResultEnum Create(IProjectCommandOptions options)
        {
            var path = options.Path;
            var displayName = options.DisplayName;
            if (string.IsNullOrEmpty(path))
            {
                if (options.Silent)
                {
                    _reportService.Error($"Path to spocr.json is required");
                    _reportService.Output($"\tPlease use '--path'");
                    return ExecuteResultEnum.Error;
                }
                else
                {
                    path = Prompt.GetString("Enter path to spocr.json, e.g. base directory of your project:", new DirectoryInfo(Directory.GetCurrentDirectory()).Name);
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return ExecuteResultEnum.Aborted;
            }

            path = CreateConfigFilePath(path);

            if (string.IsNullOrEmpty(displayName))
            {
                displayName = CreateDisplayNameFromPath(path);
            }

            if (string.IsNullOrEmpty(displayName))
            {
                _reportService.Error($"DisplayName for project is required");
                _reportService.Output($"\tPlease use '--name'");
                return ExecuteResultEnum.Error;
            }

            if (IsDisplayNameAlreadyUsed(displayName, options))
            {
                return ExecuteResultEnum.Error;
            }

            _globalConfigFile.Config?.Projects.Add(new GlobalProjectConfigurationModel
            {
                DisplayName = displayName,
                ConfigFile = path
            });

            _globalConfigFile.Save(_globalConfigFile.Config);

            if (options.Silent)
            {
                _reportService.Output($"{{ \"displayName\": \"{displayName}\" }}");
            }
            else
            {
                _reportService.Output($"Project '{displayName}' created.");
            }
            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum Update(IProjectUpdateCommandOptions options)
        {
            var displayName = options.DisplayName;
            var projectIndex = FindIndexByName(displayName);

            if (projectIndex < 0)
            {
                _reportService.Error($"Cant find project '{displayName}'");
                return ExecuteResultEnum.Error;
            }

            var path = options.Path;
            if (!string.IsNullOrEmpty(path))
            {
                path = CreateConfigFilePath(path);
            }

            var newDisplayName = options.NewDisplayName;
            if (!string.IsNullOrEmpty(newDisplayName))
            {
                if (IsDisplayNameAlreadyUsed(newDisplayName, options))
                {
                    return ExecuteResultEnum.Error;
                }
            }

            if (!string.IsNullOrEmpty(newDisplayName))
            {
                _globalConfigFile.Config.Projects[projectIndex].DisplayName = newDisplayName;
            }

            if (!string.IsNullOrEmpty(path))
            {
                _globalConfigFile.Config.Projects[projectIndex].ConfigFile = path;
            }

            _globalConfigFile.Save(_globalConfigFile.Config);

            _reportService.Output($"Project '{newDisplayName ?? displayName}' updated.");
            return ExecuteResultEnum.Succeeded;

        }

        public ExecuteResultEnum Delete(IProjectCommandOptions options)
        {
            var displayName = options.DisplayName;
            var projectIndex = FindIndexByName(displayName);

            if (projectIndex < 0)
            {
                _reportService.Error($"Cant find project '{displayName}'");
                return ExecuteResultEnum.Error;
            }

            if (!options.Force)
            {
                if (options.Silent)
                {
                    _reportService.Warn($"Please add --force to delete project '{displayName}'");
                    return ExecuteResultEnum.Error;
                }
                else
                {
                    var delete = Prompt.GetYesNo($"Delete project '{displayName}'", false, System.ConsoleColor.Red);
                    if (!delete) return ExecuteResultEnum.Aborted;
                }
            }

            _globalConfigFile.Config.Projects.RemoveAt(projectIndex);

            _globalConfigFile.Save(_globalConfigFile.Config);

            _reportService.Output($"Project '{displayName}' deleted.");
            return ExecuteResultEnum.Succeeded;
        }

        public ExecuteResultEnum List(ICommandOptions options)
        {
            var projects = _globalConfigFile.Config?.Projects;

            if (!options.Silent && !(projects?.Any() ?? false))
            {
                _reportService.Warn($"No Projects found");
                return ExecuteResultEnum.Aborted;
            }

            _reportService.Output($"[{(projects.Count > 0 ? "{" : "")}");
            projects.ForEach(project =>
            {
                var fileExists = File.Exists(project.ConfigFile).ToString().ToLower();
                _reportService.Output($"\t\"displayName\": \"{project.DisplayName}\",");
                _reportService.Output($"\t\"path\": \"{project.ConfigFile}\",");
                _reportService.Output($"\t\"fileExists\": {fileExists}");
                if (projects.FindIndex(_ => _ == project) < projects.Count - 1)
                {
                    _reportService.Output("}, {");
                }
            });
            _reportService.Output($"{(projects.Count > 0 ? "}" : "")}]");

            return ExecuteResultEnum.Succeeded;
        }

        private string CreateConfigFilePath(string path)
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Name != Configuration.ConfigurationFile)
            {
                path = path.EndsWith("/") ? path : $"{path}/";
                path = Path.GetDirectoryName(path);
                path = $"{Path.Combine(path, Configuration.ConfigurationFile)}";
            }
            return path?.Replace("\\", "/");
        }

        private string CreateDisplayNameFromPath(string path)
        {
            var fileInfo = new FileInfo(path);

            // Remove any File
            path = Path.GetDirectoryName(path);

            // Get the last DirectoryName
            var displayName = Path.GetFileName(path);
            return displayName;
        }

        private GlobalProjectConfigurationModel FindByName(string displayName)
        {
            var projects = _globalConfigFile.Config?.Projects;
            return projects?.Find(project => project.DisplayName.Equals(displayName));
        }

        private int FindIndexByName(string displayName)
        {
            var projects = _globalConfigFile.Config?.Projects;
            return projects.FindIndex(project => project.DisplayName.Equals(displayName));
        }

        private bool IsDisplayNameAlreadyUsed(string displayName, IProjectCommandOptions options)
        {
            if (FindByName(displayName) != null)
            {
                _reportService.Error($"Project {displayName} already exists");
                if (!options.Silent)
                {
                    _reportService.Output($"\tPlease run '{Configuration.Name} project ls' to show all existing projects");
                }
                return true;
            }

            return false;
        }
    }
}