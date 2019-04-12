using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.Extensions
{
    internal static class CompilationUnitSyntaxExtensions
    {
        internal static CompilationUnitSyntax ReplaceNamespace(this CompilationUnitSyntax root, Func<string, string> replacer) 
        {
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var nsValue = replacer.Invoke(nsNode.Name.ToString());
            var fullSchemaName = SyntaxFactory.ParseName($"{nsValue}{Environment.NewLine}");
            return root.ReplaceNode(nsNode, nsNode.WithName(fullSchemaName));
        }

        internal static CompilationUnitSyntax ReplaceClassName(this CompilationUnitSyntax root, Func<string, string> replacer, Func<NamespaceDeclarationSyntax, ClassDeclarationSyntax> selector = null) 
        {
            var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
            var classNode = selector != null
                ? selector.Invoke(nsNode)
                : (ClassDeclarationSyntax)nsNode.Members[0];
            var cnValue = replacer.Invoke(classNode.Identifier.ValueText);    
            var classIdentifier = SyntaxFactory.ParseToken($"{cnValue}{Environment.NewLine}");
            return root.ReplaceNode(classNode, classNode.WithIdentifier(classIdentifier));
        }

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