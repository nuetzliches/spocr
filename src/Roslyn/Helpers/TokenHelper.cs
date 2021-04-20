using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SpocR.Roslyn.Helpers
{
    public static class ExpressionHelper
    {
        public const string NewLine = "\r\n";
        public static SyntaxTrivia EndOfLineTrivia => SyntaxFactory.EndOfLine(NewLine);

        public static ExpressionStatementSyntax AssignmentStatement(string left, string right)
        {
            return SyntaxFactory.ExpressionStatement(AssignmentExpression(left, right))
                       .NormalizeWhitespace(elasticTrivia: true)
                       .AppendNewLine();
        }

        public static ExpressionSyntax AssignmentExpression(string left, string right, string propType = null, bool verifyRightNotNull = false)
        {
            var rightMemberAccess = right.ToMemberAccess();
            var rightExp = verifyRightNotNull ? rightMemberAccess.WrapInConditional(propType) : rightMemberAccess;

            return SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                left.ToMemberAccess().AppendWhitespace(),
                rightExp.PrependWhitespace());
        }

        public static ExpressionSyntax ToMemberAccess(this string selector)
        {
            var parts = selector.Split('.');

            if (parts.Count() == 1)
            {
                return SyntaxFactory.IdentifierName(parts.First());
            }
            else if (parts.Count() == 2)
            {
                if (parts.First() == "this")
                {
                    return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ThisExpression(),
                        SyntaxFactory.IdentifierName(parts.Last()));
                }

                return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(parts.First()),
                        SyntaxFactory.IdentifierName(parts.Last()));
            }
            else
            {
                var leftPart = string.Join(".", parts.Take(parts.Count() - 1));
                return SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        leftPart.ToMemberAccess(),
                        SyntaxFactory.IdentifierName(parts.Last()));
            }
        }

        public static ExpressionSyntax WrapInConditional(this ExpressionSyntax expression, string propType)
        {
            var notNullExpressions = new List<BinaryExpressionSyntax>();

            var memberAcc = expression as MemberAccessExpressionSyntax;
            while (memberAcc != null && memberAcc.Expression is MemberAccessExpressionSyntax)
            {
                var notNullExp = SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression,
                    memberAcc.Expression,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
                notNullExpressions.Add(notNullExp);

                memberAcc = memberAcc.Expression as MemberAccessExpressionSyntax;
            }

            notNullExpressions.Reverse();

            if (notNullExpressions.Count == 0)
                return expression;

            ExpressionSyntax current = notNullExpressions.First();
            for (int i = 1; i < notNullExpressions.Count; i++)
            {
                current = SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression,
                    current,
                    notNullExpressions[i]);
            }

            var fallbackExpression = propType == null ? (ExpressionSyntax)SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
               : SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(propType));

            return SyntaxFactory.ConditionalExpression(current, expression, fallbackExpression).NormalizeWhitespace();
        }

        public static TNode AppendNewLine<TNode>(this TNode node, bool preserveExistingTrivia = true)
                    where TNode : SyntaxNode
        {
            var triviaList = preserveExistingTrivia == true ? node.GetTrailingTrivia() : SyntaxFactory.TriviaList();
            triviaList = triviaList.Add(ExpressionHelper.EndOfLineTrivia);

            return node.WithTrailingTrivia(triviaList);
        }

        public static SyntaxToken AppendNewLine(this SyntaxToken token)
        {
            return token.WithTrailingTrivia(token.TrailingTrivia.Add(EndOfLineTrivia));
        }

        public static TNode AppendWhitespace<TNode>(this TNode node)
            where TNode : SyntaxNode
        {
            return node.WithTrailingTrivia(node.GetTrailingTrivia().Add(SyntaxFactory.Whitespace(" ")));
        }

        public static TNode PrependWhitespace<TNode>(this TNode node)
            where TNode : SyntaxNode
        {
            return node.WithLeadingTrivia(node.GetLeadingTrivia().Add(SyntaxFactory.Whitespace(" ")));
        }
    }
}