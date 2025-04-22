using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using SpocR.Utils;

namespace SpocR.Commands.Spocr;

[HelpOption("-?|-h|--help")]
[Command("build", Description = "Build DataContext depending on spocr.json")]
public class BuildCommand(
    SpocrManager spocrManager,
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
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

        await base.OnExecuteAsync();
        return (int)await spocrManager.BuildAsync(CommandOptions);
    }
}
