using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.Extensions
{
    internal static class ClassDeclarationSyntaxExtensions
    {
        internal static ConstructorDeclarationSyntax CreateConstructor(this ClassDeclarationSyntax classDeclaration, string name)
        {
            var constructorIdentifier = SyntaxFactory.Identifier(name);
            var constructorDeclaration =
                SyntaxFactory.ConstructorDeclaration(constructorIdentifier)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddBodyStatements()
                .NormalizeWhitespace();

            return constructorDeclaration;
        }

        internal static PropertyDeclarationSyntax CreateProperty(this ClassDeclarationSyntax classDeclaration, TypeSyntax type, string name)
        {
            var propertyIdentifier = SyntaxFactory.Identifier(name);
            var propertyDeclaration =
                SyntaxFactory.PropertyDeclaration(type, propertyIdentifier)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                )
                .NormalizeWhitespace();

            return propertyDeclaration;
        }
    }
}