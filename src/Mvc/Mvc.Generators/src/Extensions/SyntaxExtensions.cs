// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.Linq;

namespace Microsoft.AspNetCore.Mvc.Generators.Extensions;

internal static class SyntaxExtensions
{
    internal static ExpressionSyntax ToObjectCreationExpression(this AttributeData attributeData)
    {
        // returns => new {AttributeName}(args) { NamedArg = NamedArgValue };
        var constructorArguments = attributeData.ConstructorArguments
            .Select(arg => Argument(arg.Value.ToLiteralExpressionSyntax()));

        var argumentList = ArgumentList(SeparatedList(constructorArguments));

        var objectCreationExpression = ObjectCreationExpression(
                ParseTypeName(attributeData.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            .WithArgumentList(argumentList);

        if (attributeData.NamedArguments.Length > 0)
        {
            var namedArguments = attributeData.NamedArguments
                .Select(arg =>
                    (ExpressionSyntax)AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        IdentifierName(arg.Key),
                        arg.Value.Value.ToLiteralExpressionSyntax()));

            var initializer =
                InitializerExpression(SyntaxKind.ObjectInitializerExpression, SeparatedList(namedArguments));

            objectCreationExpression = objectCreationExpression.WithInitializer(initializer);
        }

        return objectCreationExpression;
    }

    internal static ExpressionSyntax ToObjectCreationExpression(this ICollection<AttributeData> attributes)
    {
        // returns => new List<object> { new attribute1(), new attribute2() };
        var listIdentifier = Identifier("global::System.Collections.Generic.List");
        var objectTypeIdentifier = PredefinedType(Token(SyntaxKind.ObjectKeyword));
        var attributeInitializers = InitializerExpression(
            SyntaxKind.CollectionInitializerExpression,
            SeparatedList(attributes.Select(arg => arg.ToObjectCreationExpression())));

        return ObjectCreationExpression(
                GenericName(listIdentifier)
                    .WithTypeArgumentList(TypeArgumentList(SingletonSeparatedList<TypeSyntax>(objectTypeIdentifier))))
            .WithInitializer(attributeInitializers);
    }

    internal static LiteralExpressionSyntax ToLiteralExpressionSyntax(this object value)
    {
        if (value is string str)
        {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(str));
        }

        // This could be simplified with INumber in .Net 7
        if (value is int integer)
        {
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(integer));
        }

        if (value is byte numeric)
        {
            return LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(numeric));
        }

        throw new NotSupportedException("Unsupported argument type");
    }

    internal static ExpressionStatementSyntax AddToList(
        this LocalDeclarationStatementSyntax controllerModel,
        string collectionName,
        params ExpressionSyntax[] parameters)
    {
        return ExpressionStatement(InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
                        IdentifierName(collectionName)),
                    IdentifierName("Add")))
            .WithArgumentList(
                ArgumentList(
                    SeparatedList(parameters.Select(Argument)))));
    }
}
