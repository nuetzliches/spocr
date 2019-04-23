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
#endif
            var codeBase = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            return Regex.Replace(codeBase, @"^(file\:\\)", string.Empty);
        }

        internal static string GetWorkingDirectory(params string[] paths)
        {
            var pathList = new List<string>();
            pathList.Add(Directory.GetCurrentDirectory());
            if (!string.IsNullOrEmpty(BasePath))
            {
                pathList.Add(BasePath);
            }
            pathList.AddRange(paths);
            return Path.Combine(pathList.ToArray()).ToString();
        }
    }
}