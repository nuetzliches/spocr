
using System;
using Microsoft.CodeAnalysis.Text;

namespace SpocR.Extensions
{
    internal static class SourceTextExtensions
    {
        internal static string WithMetadataToString(this SourceText sourceText, Version version) {
            var sourceString = sourceText.ToString();
            sourceString = sourceString.Replace("@[Name]", Configuration.Name);
            sourceString = sourceString.Replace("@[Version]", version.ToVersionString());
            return sourceString;
        }
    }
}