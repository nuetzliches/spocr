using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using SpocR.Utils;
using System.Threading.Tasks;
using SpocR;
using SpocR.Infrastructure;
using System.IO;

namespace SpocR.Commands.Spocr;

public class SpocrCommandBase(
    SpocrProjectManager spocrProjectManager
) : CommandBase
{
    [Option("-pr|--project", "Name of project that has path to spocr.json", CommandOptionType.SingleValue)]
    public string Project { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        // Read Path to spocr.json from Project configuration
        if (!string.IsNullOrEmpty(Project))
        {
            var project = spocrProjectManager.FindByName(Project);
            if (project != null)
                Path = project.ConfigFile;
            else
                throw new CliValidationException($"Project '{Project}' was not found. Run '{Constants.Name} project ls' to list configured projects or pass an absolute path via --path.");
        }
        else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
        {
            if (!PointsToExistingLocation(Path))
            {
                var project = spocrProjectManager.FindByName(Path);
                if (project == null)
                    throw new CliValidationException($"Project '{Path}' was not found. Run '{Constants.Name} project ls' to list configured projects or pass an absolute path via --path.");

                Path = project.ConfigFile;
            }
        }

        return await base.OnExecuteAsync();
    }

    private static bool PointsToExistingLocation(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            if (System.IO.Path.IsPathRooted(candidate))
            {
                return Directory.Exists(candidate) || File.Exists(candidate);
            }

            var absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory.GetCurrentDirectory(), candidate));
            return Directory.Exists(absolute) || File.Exists(absolute);
        }
        catch
        {
            return false;
        }
    }
}
