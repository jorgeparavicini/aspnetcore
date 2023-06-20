// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.AspNetCore.Mvc.Generators.Generators;

internal static class ApplicationModelProviderGenerator
{
    private const string ApplicationModelProviderInterface =
        "global::Microsoft.AspNetCore.Mvc.ApplicationModels.IApplicationModelProvider";

    private const string ApplicationModelProviderContext =
        "global::Microsoft.AspNetCore.Mvc.ApplicationModels.ApplicationModelProviderContext";

    private const int OrderPivot = -1000;

    internal static CompilationUnitSyntax? CreateApplicationModelProvider(
        ClassDeclarationSyntax sourceControllerClass,
        SemanticModel semanticModel,
        Compilation compilation,
        int normalizedOrder)
    {
        if (semanticModel.GetDeclaredSymbol(sourceControllerClass) is not { } controllerSymbol)
        {
            return null;
        }

        var targetClass = ClassDeclaration($"{sourceControllerClass.Identifier}ApplicationModelProvider")
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithBaseList(
                BaseList(
                    SingletonSeparatedList<BaseTypeSyntax>(
                        SimpleBaseType(
                            IdentifierName(ApplicationModelProviderInterface)))));

        AddOrder(normalizedOrder, ref targetClass);
        AddOnProvidersExecuting(controllerSymbol, compilation, ref targetClass);
        AddOnProvidersExecuted(ref targetClass);

        var compilationUnit = CompilationUnit()
            .WithMembers(SingletonList<MemberDeclarationSyntax>(targetClass))
            .WithUsings(List(new[] { UsingDirective(IdentifierName("System.Reflection")) }));

        return compilationUnit;
    }

    private static void AddOrder(int normalizedOrder, ref ClassDeclarationSyntax targetClass)
    {
        var property = PropertyDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)), Identifier("Order"))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithExpressionBody(
                ArrowExpressionClause(LiteralExpression(SyntaxKind.NumericLiteralExpression,
                    Literal(OrderPivot + normalizedOrder))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        targetClass = targetClass.AddMembers(property);
    }

    private static void AddOnProvidersExecuting(
        INamedTypeSymbol controllerSymbol,
        Compilation compilation,
        ref ClassDeclarationSyntax targetClass)
    {
        var method = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)),
                Identifier("OnProvidersExecuting"))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                ParameterList(
                    SingletonSeparatedList(Parameter(Identifier("context"))
                        .WithType(IdentifierName(ApplicationModelProviderContext)))))
            .WithBody(Block());

        var controllerModel = AddControllerModel(controllerSymbol, compilation, ref method, ref targetClass);
        AddControllerToContext(controllerModel, ref method);
        SetControllerModelApplication(controllerModel, ref method);

        foreach (var actionMethod in controllerSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            var actionModelCreator = ActionGenerator.AddActionModel(actionMethod, compilation, ref targetClass);
            if (actionModelCreator is null)
            {
                continue;
            }

            var actionModel = AddActionModelAssignment(actionModelCreator, ref method);
            AddActionToController(actionModel, controllerModel, ref method);
            SetActionModelController(actionModel, controllerModel, ref method);
        }

        targetClass = targetClass.AddMembers(method);
    }

    private static void AddOnProvidersExecuted(ref ClassDeclarationSyntax targetClass)
    {
        var method = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)),
                Identifier("OnProvidersExecuted"))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                ParameterList(
                    SingletonSeparatedList(Parameter(Identifier("context"))
                        .WithType(IdentifierName(ApplicationModelProviderContext)))))
            .WithBody(Block()) as MemberDeclarationSyntax;

        targetClass = targetClass.AddMembers(method);
    }

    private static LocalDeclarationStatementSyntax AddControllerModel(
        INamedTypeSymbol controllerSymbol,
        Compilation compilation,
        ref MethodDeclarationSyntax method,
        ref ClassDeclarationSyntax targetClass)
    {
        var createControllerMethod =
            ControllerGenerator.AddControllerModel(controllerSymbol, compilation, ref targetClass);

        var controllerModel = LocalDeclarationStatement(
            VariableDeclaration(
                    IdentifierName("var"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                                Identifier("controllerModel"))
                            .WithInitializer(
                                EqualsValueClause(
                                    InvocationExpression(IdentifierName(createControllerMethod.Identifier.Text)))))));

        method = method.AddBodyStatements(controllerModel);

        return controllerModel;
    }

    private static void AddControllerToContext(
        LocalDeclarationStatementSyntax controllerModel,
        ref MethodDeclarationSyntax method)
    {
        var assignment = ExpressionStatement(
            InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("context"),
                                IdentifierName("Result")),
                            IdentifierName("Controllers")),
                        IdentifierName("Add")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text))))));

        method = method.AddBodyStatements(assignment);
    }

    private static void SetControllerModelApplication(
        LocalDeclarationStatementSyntax controllerModel,
        ref MethodDeclarationSyntax method)
    {
        var expression = ExpressionStatement(
            AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
                    IdentifierName("Application")),
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("context"),
                    IdentifierName("Result"))));

        method = method.AddBodyStatements(expression);
    }

    private static LocalDeclarationStatementSyntax AddActionModelAssignment(
        MethodDeclarationSyntax createActionMethod,
        ref MethodDeclarationSyntax method)
    {
        var variableName = $"actionName{Guid.NewGuid():N}";
        var actionModel = LocalDeclarationStatement(
            VariableDeclaration(
                    IdentifierName("var"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(
                                Identifier(variableName))
                            .WithInitializer(
                                EqualsValueClause(
                                    InvocationExpression(IdentifierName(createActionMethod.Identifier.Text)))))));

        method = method.AddBodyStatements(actionModel);

        return actionModel;
    }

    private static void AddActionToController(
        LocalDeclarationStatementSyntax actionModel,
        LocalDeclarationStatementSyntax controllerModel,
        ref MethodDeclarationSyntax method)
    {
        var assignment = ExpressionStatement(
            InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
                            IdentifierName("Actions")),
                        IdentifierName("Add")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text))))));

        method = method.AddBodyStatements(assignment);
    }

    private static void SetActionModelController(
        LocalDeclarationStatementSyntax actionModel,
        LocalDeclarationStatementSyntax controllerModel,
        ref MethodDeclarationSyntax method)
    {
        var expression = ExpressionStatement(
            AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text),
                    IdentifierName("Controller")),
                IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text)));

        method = method.AddBodyStatements(expression);
    }
}
