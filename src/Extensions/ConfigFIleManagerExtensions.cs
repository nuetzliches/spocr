using System.IO;
using System.Linq;
using SpocR.Managers;

namespace SpocR.Extensions
{
    public static class ConfigFIleManagerExtensions
    {
        public static string GetDataContextNamespace(this ConfigFileManager config, string dataContextIdentifier = "DataContext")
        {
           var dataContextNode = config.Config.Project.Output.SingleOrDefault(i => i.Name.Equals(dataContextIdentifier));
            var path = dataContextNode.Path.Replace("./", "");
            path = Path.Combine(dataContextNode.Namespace, path);
            return path.Replace('\\', '.');
        }
    }
}