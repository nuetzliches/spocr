using System;
using Microsoft.CodeAnalysis.Text;
using SpocR.Internal.Common;
using SpocR.Internal.Models;

namespace SpocR.Internal.Extensions
{
    internal static class SourceTextExtensions
    {
        internal static string WithMetadataToString(this SourceText sourceText, Version version) {
            var sourceString = sourceText.ToString();
            sourceString = sourceString.Replace("@[Name]", Configuration.Name);
            sourceString = sourceString.Replace("@[Version]", version.ToVersionString());
            sourceString = sourceString.Replace("@[LastModified]", DateTime.Now.ToString());
            sourceString = sourceString.Replace("@[Locked]", false.ToString());
            return sourceString;
        }
    }
}