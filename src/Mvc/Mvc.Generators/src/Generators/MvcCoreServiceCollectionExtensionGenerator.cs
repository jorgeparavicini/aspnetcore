// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.AspNetCore.Mvc.Generators.Generators;

internal static class MvcCoreServiceCollectionExtensionGenerator
{
    private const string ClassName = "MvcCoreServiceCollectionExtensions";
    private const string MethodName = "AddStaticMvcCore";
    private const string ParameterName = "services";

    private const string ApplicationModelProviderInterface =
        "global::Microsoft.AspNetCore.Mvc.ApplicationModels.IApplicationModelProvider";

    private const string ServiceDescriptor = "global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor";

    internal static CompilationUnitSyntax CreateMvcCoreServiceCollectionExtension(
        IEnumerable<CompilationUnitSyntax> applicationModelProviders)
    {
        var argumentNullCheckStatement = ExpressionStatement(
            InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName("ArgumentNullException"),
                        IdentifierName("ThrowIfNull")))
                .WithArgumentList(
                    ArgumentList(SingletonSeparatedList(Argument(IdentifierName(ParameterName))))));

        var builderStatement = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier("builder"))
                            .WithInitializer(EqualsValueClause(InvocationExpression(MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(ParameterName),
                                    IdentifierName("AddMvcCore")))
                                .WithArgumentList(
                                    ArgumentList(SingletonSeparatedList(
                                        Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression))))))))));

        var method = MethodDeclaration(
                IdentifierName("global::Microsoft.Extensions.DependencyInjection.IMvcCoreBuilder"),
                Identifier(MethodName))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(
                ParameterList(
                    SingletonSeparatedList(
                        Parameter(Identifier(ParameterName))
                            .WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword)))
                            .WithType(
                                QualifiedName(
                                    QualifiedName(
                                        QualifiedName(
                                            IdentifierName("Microsoft"),
                                            IdentifierName("Extensions")),
                                        IdentifierName("DependencyInjection")),
                                    IdentifierName("IServiceCollection"))))))
            .WithBody(Block(argumentNullCheckStatement, builderStatement));

        foreach (var applicationModelProvider in applicationModelProviders)
        {
            method = method.AddBodyStatements(AddModelProviderToServicesStatement(applicationModelProvider));
        }

        var returnStatement =
            ReturnStatement(IdentifierName(builderStatement.Declaration.Variables.First().Identifier.Text));
        method = method.AddBodyStatements(returnStatement);

        var classDeclaration = ClassDeclaration(ClassName)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(method));

        var compilationUnit = CompilationUnit()
            .WithUsings(List(new []
            {
                UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")),
                UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection.Extensions"))
            }))
            .WithMembers(SingletonList<MemberDeclarationSyntax>(classDeclaration));

        return compilationUnit;
    }

    private static ExpressionStatementSyntax AddModelProviderToServicesStatement(
        SyntaxNode applicationModelProvider)
    {
        var modelProviderType = applicationModelProvider.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First()
            .Identifier.Text;
        var addModelProvider = InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(ParameterName),
                    IdentifierName("TryAddEnumerable")))
            .WithArgumentList(
                ArgumentList(SingletonSeparatedList(Argument(
                    InvocationExpression(MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(ServiceDescriptor),
                        GenericName(Identifier("Transient"))
                            .WithTypeArgumentList(TypeArgumentList(SeparatedList<TypeSyntax>(
                                new SyntaxNodeOrToken[]
                                {
                                    IdentifierName(ApplicationModelProviderInterface), Token(SyntaxKind.CommaToken),
                                    IdentifierName(modelProviderType)
                                })))))))));

        return ExpressionStatement(addModelProvider);
    }
}
