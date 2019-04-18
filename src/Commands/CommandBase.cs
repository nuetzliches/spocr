using McMaster.Extensions.CommandLineUtils;
using SpocR.Enums;
using SpocR.Managers;
using SpocR.Utils;

namespace SpocR.Commands
{
    public abstract class CommandBase : IAppCommand
    {
        [Option("-p|--path", "Path where the generated spocr.json will be generated, eg. the path to your project itself", CommandOptionType.SingleValue)]
        public virtual string Path { get; set; }


        [Option("-d|--dry-run", "Run build without any changes", CommandOptionType.NoValue)]
        public virtual bool DryRun { get; set; }

        public virtual int OnExecute()
        {
            DirectoryUtils.SetBasePath(Path);
            return (int)ExecuteResultEnum.Succeeded; ;
        }
    }
}
