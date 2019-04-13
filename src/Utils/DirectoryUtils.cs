using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SpocR.Utils
{
    public static class DirectoryUtils 
    {
        public static string GetApplicationRoot()
        {
#if DEBUG
            return Directory.GetCurrentDirectory();
#endif
            var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            return Regex.Replace(codeBase, @"^(file\:\\)", string.Empty);
        }
    }
}