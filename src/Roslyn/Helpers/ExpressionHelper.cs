using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpocR.Roslyn.Helpers
{
    public static class TokenHelper
    {
        public static SyntaxToken Parse(string identyfier)
        {
            var newIdentifier = identyfier.Replace("@", "");
            return SyntaxFactory.ParseToken($" {newIdentifier} ");
        }
    }
}