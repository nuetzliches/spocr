using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Extensions;
using SpocR.Roslyn.Helpers;

namespace SpocR.CodeGenerators.Extensions;

/// <summary>
/// Spezialisierte Erweiterungsmethoden für die Arbeit mit Roslyn im Generator-Kontext
/// </summary>
public static class RoslynGeneratorExtensions
{
    /// <summary>
    /// Fügt mehrere Using-Direktiven zu einem Root-Element hinzu
    /// </summary>
    public static CompilationUnitSyntax AddMultipleUsings(this CompilationUnitSyntax root, IEnumerable<string> namespaces, string prefix = null)
    {
        var result = root;
        foreach (var ns in namespaces)
        {
            string fullNamespace = prefix != null ? $"{prefix}.{ns}" : ns;
            var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(fullNamespace));
            result = result.AddUsings(directive).NormalizeWhitespace();
        }
        return result;
    }

    /// <summary>
    /// Erstellt eine Property mit optionalen Attributen
    /// </summary>
    public static PropertyDeclarationSyntax CreatePropertyWithAttributes(
        this ClassDeclarationSyntax classNode,
        TypeSyntax type,
        string name,
        Dictionary<string, object> attributeValues = null)
    {
        // Property erstellen
        var property = classNode.CreateProperty(type, name);

        // Attribute hinzufügen
        if (attributeValues != null && attributeValues.Count > 0)
        {
            var attributeList = SyntaxFactory.AttributeList();
            foreach (var attr in attributeValues)
            {
                var attribute = SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName(attr.Key),
                    SyntaxFactory.ParseAttributeArgumentList($"({attr.Value})"));

                attributeList = attributeList.AddAttributes(attribute);
            }
            property = property.AddAttributeLists(attributeList.NormalizeWhitespace());
        }

        return property;
    }

    /// <summary>
    /// Fügt einer Klasse einen Konstruktor mit einem Parameter hinzu
    /// </summary>
    public static CompilationUnitSyntax AddParameterizedConstructor(
        this CompilationUnitSyntax root,
        string className,
        IEnumerable<(string TypeName, string ParamName, string PropertyName)> parameters)
    {
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Constructor erstellen
        var constructor = classNode.CreateConstructor(className);

        // Parameter hinzufügen
        var paramList = constructor.ParameterList;
        var statements = new List<StatementSyntax>();

        foreach (var (typeName, paramName, propertyName) in parameters)
        {
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(typeName));

            paramList = paramList.AddParameters(parameter);

            // Zuweisung zum Property erstellen
            var assignment = ExpressionHelper.AssignmentStatement(propertyName, paramName);
            statements.Add(assignment);
        }

        // Konstruktor aktualisieren
        constructor = constructor
            .WithParameterList(paramList)
            .WithBody(constructor.Body.WithStatements(SyntaxFactory.List(statements)));

        // Konstruktor zur Klasse hinzufügen
        root = root.AddConstructor(ref classNode, constructor);

        return root;
    }

    /// <summary>
    /// Fügt eine obsolete Annotation mit Nachricht hinzu
    /// </summary>
    public static TNode AddObsoleteAttribute<TNode>(this TNode node, string message) where TNode : MemberDeclarationSyntax
    {
        var obsoleteAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("Obsolete"),
            SyntaxFactory.ParseAttributeArgumentList($"(\"{message}\")"));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(obsoleteAttribute))
            .NormalizeWhitespace();

        return (TNode)node.AddAttributeLists(attributeList);
    }
}