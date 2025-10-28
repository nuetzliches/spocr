using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using SpocR.Utils;
using System.Threading.Tasks;

namespace SpocR.Commands.Spocr;

public class SpocrCommandBase(
    SpocrProjectManager spocrProjectManager
) : CommandBase
{
    [Option("-pr|--project", "Legacy project alias (maps to stored config path); prefer --path pointing at your .env root.", CommandOptionType.SingleValue)]
    public string Project { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        // Map legacy project alias back to stored path (kept for bridge users)
        if (!string.IsNullOrEmpty(Project))
        {
            var project = spocrProjectManager.FindByName(Project);
            if (project != null)
                Path = project.ConfigFile;
        }
        else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
        {
            var project = spocrProjectManager.FindByName(Path);
            if (project != null)
            {
                Path = project.ConfigFile;
            }
        }

        return await base.OnExecuteAsync();
    }
}
