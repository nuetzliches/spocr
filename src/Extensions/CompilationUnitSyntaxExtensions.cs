using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.Extensions
{
    internal static class CompilationUnitSyntaxExtensions
    {
        internal static CompilationUnitSyntax ReplaceUsings(this CompilationUnitSyntax root, Func<string, string> replacer)
        {
            var newUsings = new SyntaxList<UsingDirectiveSyntax>();
            foreach (var u in root.Usings)
            {
                var uValue = replacer.Invoke(u.Name.ToString());
                var usingName = SyntaxFactory.ParseName(uValue);
                newUsings = newUsings.Add(u.WithName(usingName));
            }
            return root.WithUsings(newUsings);
        }

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

        internal static CompilationUnitSyntax AddProperty(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, PropertyDeclarationSyntax propertyDeclaration)
        {
            var newClass = classDeclaration.AddMembers(propertyDeclaration);
            root = root.ReplaceNode(classDeclaration, newClass);
            classDeclaration = newClass;
            return root;
        }

        internal static CompilationUnitSyntax AddMethod(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, MethodDeclarationSyntax methodDeclaration)
        {
            var newClass = classDeclaration.AddMembers(methodDeclaration);
            root = root.ReplaceNode(classDeclaration, newClass);
            classDeclaration = newClass;
            return root;
        }

        internal static CompilationUnitSyntax AddConstructor(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, ConstructorDeclarationSyntax constructorDeclaration)
        {
            var newClass = classDeclaration.AddMembers(constructorDeclaration);
            root = root.ReplaceNode(classDeclaration, newClass);
            classDeclaration = newClass;
            return root;
        }

        internal static CompilationUnitSyntax AddCustomAttribute(this CompilationUnitSyntax root, ref MethodDeclarationSyntax methodDeclaration, string name, AttributeArgumentListSyntax arguments = default)
        {
            var attributes = methodDeclaration.AttributeLists.Add(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name))
                    .WithArgumentList(arguments)
                )).NormalizeWhitespace());

            var newMethodDeclaration = methodDeclaration.WithAttributeLists(attributes);
            root = root.ReplaceNode(
                methodDeclaration,
                newMethodDeclaration
            );

            methodDeclaration = newMethodDeclaration;

            return root;
        }

        internal static CompilationUnitSyntax AddCustomAttribute(this CompilationUnitSyntax root, ref ConstructorDeclarationSyntax constructorDeclaration, string name, AttributeArgumentListSyntax arguments = default)
        {
            var attributes = constructorDeclaration.AttributeLists.Add(
                SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList<AttributeSyntax>(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name))
                    .WithArgumentList(arguments)
                )).NormalizeWhitespace());

            var newConstructorDeclaration = constructorDeclaration.WithAttributeLists(attributes);
            root = root.ReplaceNode(
                constructorDeclaration,
                newConstructorDeclaration
            );

            constructorDeclaration = newConstructorDeclaration;

            return root;
        }

        internal static CompilationUnitSyntax AddObsoleteAttribute(this CompilationUnitSyntax root, ref MethodDeclarationSyntax methodDeclaration, string message = null)
        {
            var arguments = !string.IsNullOrWhiteSpace(message) ? SyntaxFactory.ParseAttributeArgumentList($"(\"{message}\")") : default;
            return root.AddCustomAttribute(ref methodDeclaration, "Obsolete", arguments);
        }

        internal static CompilationUnitSyntax AddObsoleteAttribute(this CompilationUnitSyntax root, ref ConstructorDeclarationSyntax constructorDeclaration, string message = null)
        {
            var arguments = !string.IsNullOrWhiteSpace(message) ? SyntaxFactory.ParseAttributeArgumentList($"(\"{message}\")") : default;
            return root.AddCustomAttribute(ref constructorDeclaration, "Obsolete", arguments);
        }
    }
}