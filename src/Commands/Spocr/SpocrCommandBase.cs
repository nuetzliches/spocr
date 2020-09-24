using McMaster.Extensions.CommandLineUtils;
using SpocR.Managers;
using SpocR.Utils;

namespace SpocR.Commands.Spocr
{
    public class SpocrCommandBase : CommandBase
    {
        [Option("-pr|--project", "Name of project that has path to spocr.json", CommandOptionType.SingleValue)]
        public virtual string Project { get; set; }

        protected readonly SpocrProjectManager SpocrProjectManager;

        public SpocrCommandBase(SpocrProjectManager spocrProjectManager)
        {
            SpocrProjectManager = spocrProjectManager;
        }

        public override int OnExecute()
        {
            // Read Path to spocr.json from Project configuration
            if (!string.IsNullOrEmpty(Project))
            {
                var project = SpocrProjectManager.FindByName(Project);
                if (project != null)
                    Path = project.ConfigFile;
            } 
            else if (!string.IsNullOrEmpty(Path) && !DirectoryUtils.IsPath(Path))
            {
                var project = SpocrProjectManager.FindByName(Path);
                Path = project.ConfigFile;
            }

            return base.OnExecute();
        }
    }
}
