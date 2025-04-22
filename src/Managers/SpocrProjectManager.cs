using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Commands;
using SpocR.Commands.Project;
using SpocR.Enums;
using SpocR.Models;
using SpocR.Services;

namespace SpocR.Managers;

public class SpocrProjectManager(
    FileManager<GlobalConfigurationModel> globalConfigFile,
    IReportService reportService
)
{
    public async Task<ExecuteResultEnum> CreateAsync(IProjectCommandOptions options)
    {
        var path = options.Path;
        var displayName = options.DisplayName;
        if (string.IsNullOrEmpty(path))
        {
            if (options.Silent)
            {
                reportService.Error($"Path to spocr.json is required");
                reportService.Output($"\tPlease use '--path'");
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
            reportService.Error($"DisplayName for project is required");
            reportService.Output($"\tPlease use '--name'");
            return ExecuteResultEnum.Error;
        }

        if (IsDisplayNameAlreadyUsed(displayName, options))
        {
            return ExecuteResultEnum.Error;
        }

        globalConfigFile.Config?.Projects.Add(new GlobalProjectConfigurationModel
        {
            DisplayName = displayName,
            ConfigFile = path
        });

        await Task.Run(() => globalConfigFile.Save(globalConfigFile.Config));

        if (options.Silent)
        {
            reportService.Output($"{{ \"displayName\": \"{displayName}\" }}");
        }
        else
        {
            reportService.Output($"Project '{displayName}' created.");
        }
        return ExecuteResultEnum.Succeeded;
    }

    public ExecuteResultEnum Create(IProjectCommandOptions options)
    {
        return CreateAsync(options).GetAwaiter().GetResult();
    }

    public async Task<ExecuteResultEnum> UpdateAsync(IProjectUpdateCommandOptions options)
    {
        var displayName = options.DisplayName;
        var projectIndex = FindIndexByName(displayName);

        if (projectIndex < 0)
        {
            reportService.Error($"Cant find project '{displayName}'");
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
            globalConfigFile.Config.Projects[projectIndex].DisplayName = newDisplayName;
        }

        if (!string.IsNullOrEmpty(path))
        {
            globalConfigFile.Config.Projects[projectIndex].ConfigFile = path;
        }

        await Task.Run(() => globalConfigFile.Save(globalConfigFile.Config));

        reportService.Output($"Project '{newDisplayName ?? displayName}' updated.");
        return ExecuteResultEnum.Succeeded;
    }

    public ExecuteResultEnum Update(IProjectUpdateCommandOptions options)
    {
        return UpdateAsync(options).GetAwaiter().GetResult();
    }

    public async Task<ExecuteResultEnum> DeleteAsync(IProjectCommandOptions options)
    {
        var displayName = options.DisplayName;
        var projectIndex = FindIndexByName(displayName);

        if (projectIndex < 0)
        {
            reportService.Error($"Cant find project '{displayName}'");
            return ExecuteResultEnum.Error;
        }

        if (!options.Force)
        {
            if (options.Silent)
            {
                reportService.Warn($"Please add --force to delete project '{displayName}'");
                return ExecuteResultEnum.Error;
            }
            else
            {
                var delete = Prompt.GetYesNo($"Delete project '{displayName}'", false, System.ConsoleColor.Red);
                if (!delete) return ExecuteResultEnum.Aborted;
            }
        }

        globalConfigFile.Config.Projects.RemoveAt(projectIndex);

        await Task.Run(() => globalConfigFile.Save(globalConfigFile.Config));

        reportService.Output($"Project '{displayName}' deleted.");
        return ExecuteResultEnum.Succeeded;
    }

    public ExecuteResultEnum Delete(IProjectCommandOptions options)
    {
        return DeleteAsync(options).GetAwaiter().GetResult();
    }

    public async Task<ExecuteResultEnum> ListAsync(ICommandOptions options)
    {
        var projects = globalConfigFile.Config?.Projects;

        if (!options.Silent && !(projects?.Any() ?? false))
        {
            reportService.Warn($"No Projects found");
            return ExecuteResultEnum.Aborted;
        }

        await Task.Run(() =>
        {
            reportService.Output($"[{(projects.Count > 0 ? "{" : "")}");
            projects.ForEach(project =>
            {
                var fileExists = File.Exists(project.ConfigFile).ToString().ToLower();
                reportService.Output($"\t\"displayName\": \"{project.DisplayName}\",");
                reportService.Output($"\t\"path\": \"{project.ConfigFile}\",");
                reportService.Output($"\t\"fileExists\": {fileExists}");
                if (projects.FindIndex(_ => _ == project) < projects.Count - 1)
                {
                    reportService.Output("}, {");
                }
            });
            reportService.Output($"{(projects.Count > 0 ? "}" : "")}]");
        });

        return ExecuteResultEnum.Succeeded;
    }

    public ExecuteResultEnum List(ICommandOptions options)
    {
        return ListAsync(options).GetAwaiter().GetResult();
    }

    private string CreateConfigFilePath(string path)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Name != Constants.ConfigurationFile)
        {
            path = path.EndsWith("/") ? path : $"{path}/";
            path = Path.GetDirectoryName(path);
            path = $"{Path.Combine(path, Constants.ConfigurationFile)}";
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

    internal GlobalProjectConfigurationModel FindByName(string displayName)
    {
        var projects = globalConfigFile.Config?.Projects;
        return projects?.Find(project => project.DisplayName.Equals(displayName));
    }

    private int FindIndexByName(string displayName)
    {
        var projects = globalConfigFile.Config?.Projects;
        return projects.FindIndex(project => project.DisplayName.Equals(displayName));
    }

    private bool IsDisplayNameAlreadyUsed(string displayName, IProjectCommandOptions options)
    {
        if (FindByName(displayName) != null)
        {
            reportService.Error($"Project {displayName} already exists");
            if (!options.Silent)
            {
                reportService.Output($"\tPlease run '{Constants.Name} project ls' to show all existing projects");
            }
            return true;
        }

        return false;
    }
}