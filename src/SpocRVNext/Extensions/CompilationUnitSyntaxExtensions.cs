using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.SpocRVNext.Extensions;

internal static class CompilationUnitSyntaxExtensions
{
    internal static CompilationUnitSyntax ReplaceUsings(this CompilationUnitSyntax root, Func<string, string> replacer)
    {
        var updatedUsings = new SyntaxList<UsingDirectiveSyntax>();
        foreach (var directive in root.Usings)
        {
            var original = directive.Name?.ToString() ?? string.Empty;
            var replaced = replacer.Invoke(original);
            var name = SyntaxFactory.ParseName(replaced);
            updatedUsings = updatedUsings.Add(directive.WithName(name));
        }

        return root.WithUsings(updatedUsings);
    }

    internal static CompilationUnitSyntax ReplaceNamespace(this CompilationUnitSyntax root, Func<string, string> replacer)
    {
        if (root.Members[0] is FileScopedNamespaceDeclarationSyntax fileScoped)
        {
            var updated = replacer.Invoke(fileScoped.Name.ToString());
            var parsed = SyntaxFactory.ParseName(updated);
            return root.ReplaceNode(fileScoped, fileScoped.WithName(parsed));
        }

        if (root.Members[0] is NamespaceDeclarationSyntax namespaceDeclaration)
        {
            var updated = replacer.Invoke(namespaceDeclaration.Name.ToString());
            var parsed = SyntaxFactory.ParseName(updated + Environment.NewLine);
            return root.ReplaceNode(namespaceDeclaration, namespaceDeclaration.WithName(parsed));
        }

        throw new InvalidOperationException("Root must contain a namespace declaration.");
    }

    internal static CompilationUnitSyntax ReplaceClassName(this CompilationUnitSyntax root, Func<string, string> replacer, Func<BaseNamespaceDeclarationSyntax, ClassDeclarationSyntax>? selector = null)
    {
        var nsNode = root.Members[0] as BaseNamespaceDeclarationSyntax ?? throw new InvalidOperationException("Root does not contain a namespace declaration.");
        var classNode = selector != null ? selector.Invoke(nsNode) : nsNode.Members.OfType<ClassDeclarationSyntax>().First();
        var identifier = SyntaxFactory.Identifier(replacer.Invoke(classNode.Identifier.ValueText));
        return root.ReplaceNode(classNode, classNode.WithIdentifier(identifier));
    }

    internal static CompilationUnitSyntax AddProperty(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, PropertyDeclarationSyntax propertyDeclaration)
    {
        var updated = classDeclaration.AddMembers(propertyDeclaration);
        root = root.ReplaceNode(classDeclaration, updated);
        classDeclaration = updated;
        return root;
    }

    internal static CompilationUnitSyntax AddMethod(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, MethodDeclarationSyntax methodDeclaration)
    {
        var updated = classDeclaration.AddMembers(methodDeclaration);
        root = root.ReplaceNode(classDeclaration, updated);
        classDeclaration = updated;
        return root;
    }

    internal static CompilationUnitSyntax AddConstructor(this CompilationUnitSyntax root, ref ClassDeclarationSyntax classDeclaration, ConstructorDeclarationSyntax constructorDeclaration)
    {
        var updated = classDeclaration.AddMembers(constructorDeclaration);
        root = root.ReplaceNode(classDeclaration, updated);
        classDeclaration = updated;
        return root;
    }

    internal static CompilationUnitSyntax AddCustomAttribute(this CompilationUnitSyntax root, ref MethodDeclarationSyntax methodDeclaration, string name, AttributeArgumentListSyntax? arguments = null)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name));
        if (arguments is not null)
        {
            attribute = attribute.WithArgumentList(arguments);
        }

        var attributes = methodDeclaration.AttributeLists.Add(
            SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .NormalizeWhitespace());

        var updated = methodDeclaration.WithAttributeLists(attributes);
        root = root.ReplaceNode(methodDeclaration, updated);
        methodDeclaration = updated;
        return root;
    }

    internal static CompilationUnitSyntax AddCustomAttribute(this CompilationUnitSyntax root, ref ConstructorDeclarationSyntax constructorDeclaration, string name, AttributeArgumentListSyntax? arguments = null)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(name));
        if (arguments is not null)
        {
            attribute = attribute.WithArgumentList(arguments);
        }

        var attributes = constructorDeclaration.AttributeLists.Add(
            SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .NormalizeWhitespace());

        var updated = constructorDeclaration.WithAttributeLists(attributes);
        root = root.ReplaceNode(constructorDeclaration, updated);
        constructorDeclaration = updated;
        return root;
    }

    internal static CompilationUnitSyntax AddObsoleteAttribute(this CompilationUnitSyntax root, ref MethodDeclarationSyntax methodDeclaration, string? message = null)
    {
        var arguments = !string.IsNullOrWhiteSpace(message) ? SyntaxFactory.ParseAttributeArgumentList($"(\"{message}\")") : null;
        return root.AddCustomAttribute(ref methodDeclaration, "Obsolete", arguments);
    }

    internal static CompilationUnitSyntax AddObsoleteAttribute(this CompilationUnitSyntax root, ref ConstructorDeclarationSyntax constructorDeclaration, string? message = null)
    {
        var arguments = !string.IsNullOrWhiteSpace(message) ? SyntaxFactory.ParseAttributeArgumentList($"(\"{message}\")") : null;
        return root.AddCustomAttribute(ref constructorDeclaration, "Obsolete", arguments);
    }
}
