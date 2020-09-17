using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SpocR.Utils
{
    internal static class DirectoryUtils
    {
        private static string BasePath;

        internal static void SetBasePath(string path)
        {
            BasePath = path;
        }

        internal static string GetApplicationRoot()
        {
#if DEBUG
            return Directory.GetCurrentDirectory();
#else
            var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            return Regex.Replace(codeBase, @"^(file\:\\)", string.Empty);
#endif   
        }

        internal static string GetAppDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spocr");
        }

        internal static string GetWorkingDirectory(params string[] paths)
        {
            var pathList = new List<string>();
#if DEBUG
            pathList.Add(Path.Combine(Directory.GetCurrentDirectory(), "..", "debug"));
#else
            pathList.Add(Directory.GetCurrentDirectory());
#endif
            if (!string.IsNullOrEmpty(BasePath))
            {
                pathList.Add(BasePath);
            }
            pathList.AddRange(paths);
            return Path.Combine(pathList.ToArray()).ToString();
        }
    }
}