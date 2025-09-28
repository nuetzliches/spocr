using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SpocR.Extensions;
using SpocR.Roslyn.Helpers;

namespace SpocR.CodeGenerators.Extensions;

/// <summary>
/// Specialized extension methods for working with Roslyn in the generator context
/// </summary>
public static class RoslynGeneratorExtensions
{
    /// <summary>
    /// Adds multiple using directives to a root element
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
    /// Creates a property with optional attributes
    /// </summary>
    public static PropertyDeclarationSyntax CreatePropertyWithAttributes(
        this ClassDeclarationSyntax classNode,
        TypeSyntax type,
        string name,
        Dictionary<string, object> attributeValues = null)
    {
        // Create property
        var property = classNode.CreateProperty(type, name);

        // Add attributes
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
    /// Adds a constructor with a parameter to a class
    /// </summary>
    public static CompilationUnitSyntax AddParameterizedConstructor(
        this CompilationUnitSyntax root,
        string className,
        IEnumerable<(string TypeName, string ParamName, string PropertyName)> parameters)
    {
        var nsNode = (NamespaceDeclarationSyntax)root.Members[0];
        var classNode = (ClassDeclarationSyntax)nsNode.Members[0];

        // Create constructor
        var constructor = classNode.CreateConstructor(className);

        // Add parameter
        var paramList = constructor.ParameterList;
        var statements = new List<StatementSyntax>();

        foreach (var (typeName, paramName, propertyName) in parameters)
        {
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(typeName));

            paramList = paramList.AddParameters(parameter);

            // Create assignment for the property
            var assignment = ExpressionHelper.AssignmentStatement(propertyName, paramName);
            statements.Add(assignment);
        }

        // Update constructor
        constructor = constructor
            .WithParameterList(paramList)
            .WithBody(constructor.Body.WithStatements(SyntaxFactory.List(statements)));

        // Add constructor to the class
        root = root.AddConstructor(ref classNode, constructor);

        return root;
    }

    /// <summary>
    /// Adds an obsolete attribute with a message
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