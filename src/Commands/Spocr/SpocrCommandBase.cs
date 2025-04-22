using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using SpocR.Utils;
using System.Threading.Tasks;

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
        }
        else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
        {
            var project = spocrProjectManager.FindByName(Path);
            Path = project.ConfigFile;
        }

        return await base.OnExecuteAsync();
    }
}
