using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.Extensions
{
    internal static class CompilationUnitSyntaxExtensions
    {
        internal static CompilationUnitSyntax AddProperty(this CompilationUnitSyntax root, ClassDeclarationSyntax classDeclaration, PropertyDeclarationSyntax propertyDeclaration)
        {
            var newClass = classDeclaration.AddMembers(propertyDeclaration);
            return root.ReplaceNode(classDeclaration, newClass);
        }

        internal static CompilationUnitSyntax AddMethod(this CompilationUnitSyntax root, ClassDeclarationSyntax classDeclaration, MethodDeclarationSyntax methodDeclaration)
        {
            var newClass = classDeclaration.AddMembers(methodDeclaration);
            return root.ReplaceNode(classDeclaration, newClass);
        }
    }
}