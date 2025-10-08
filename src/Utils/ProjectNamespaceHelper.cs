using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpocR.Utils
{
    internal static class ProjectNamespaceHelper
    {
        internal static string InferRootNamespace()
        {
            try
            {
                var cwd = DirectoryUtils.GetWorkingDirectory();
                var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                string Sanitize(string raw)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return null;
                    var cleaned = Regex.Replace(raw, "[^A-Za-z0-9_.]", "_");
                    while (cleaned.Contains("__")) cleaned = cleaned.Replace("__", "_");
                    while (cleaned.Contains("..")) cleaned = cleaned.Replace("..", ".");
                    cleaned = cleaned.Trim('_', '.');
                    if (string.IsNullOrEmpty(cleaned)) cleaned = "App";
                    if (!Regex.IsMatch(cleaned.Substring(0, 1), "[A-Za-z_]")) cleaned = "App_" + cleaned;
                    return cleaned;
                }

                if (csproj != null)
                {
                    var xml = File.ReadAllText(csproj);
                    var rootNsMatch = Regex.Match(xml, "<RootNamespace>(.*?)</RootNamespace>", RegexOptions.IgnoreCase);
                    if (rootNsMatch.Success) return Sanitize(rootNsMatch.Groups[1].Value.Trim());
                    var asmMatch = Regex.Match(xml, "<AssemblyName>(.*?)</AssemblyName>", RegexOptions.IgnoreCase);
                    if (asmMatch.Success) return Sanitize(asmMatch.Groups[1].Value.Trim());
                    return Sanitize(Path.GetFileNameWithoutExtension(csproj));
                }

                return Sanitize(new DirectoryInfo(cwd).Name);
            }
            catch
            {
                return null;
            }
        }
    }
}
