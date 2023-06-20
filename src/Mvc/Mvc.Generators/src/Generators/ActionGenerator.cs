// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Generators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.AspNetCore.Mvc.Generators.Generators;

internal static class ActionGenerator
{
    internal static MethodDeclarationSyntax? AddActionModel(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        ref ClassDeclarationSyntax targetClass)
    {
        if (!IsAction(methodSymbol, compilation))
        {
            return null;
        }

        var attributes = methodSymbol.GetAttributes().ToList();
        var methodName = CreateUniqueName(methodSymbol.Name);

        var resultMethod = CreateActionMethod(methodName);

        var actionModel = AddNewActionModelStatement(methodSymbol, attributes, ref resultMethod);

        SetActionName(actionModel, methodSymbol, attributes, compilation, ref resultMethod);
        AddFilterAttributes(actionModel, attributes, compilation, ref resultMethod);
        SetApiDescriptionVisibilityProvider(actionModel, attributes, compilation, ref resultMethod);
        SetApiDescriptionGroupNameProvider(actionModel, attributes, compilation, ref resultMethod);
        AddRouteValueProviders(actionModel, attributes, compilation, ref resultMethod);

        var routingAttributes = GetRoutingAttributes(methodSymbol, compilation);
        AddSelectors(actionModel, routingAttributes, compilation, ref resultMethod, ref targetClass);

        AddReturn(actionModel, ref resultMethod);

        targetClass = targetClass.AddMembers(resultMethod);

        return resultMethod;
    }

    internal static MethodDeclarationSyntax CreateActionMethod(string actionName)
    {
        return MethodDeclaration(
                ParseTypeName("global::Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel"),
                actionName)
            .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));
    }

    internal static LocalDeclarationStatementSyntax AddNewActionModelStatement(
        IMethodSymbol methodSymbol,
        ICollection<AttributeData> attributes,
        ref MethodDeclarationSyntax method)
    {
        var actionModel = IdentifierName("global::Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel");
        var methodType =
            InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        TypeOfExpression(
                            IdentifierName(
                                methodSymbol.ContainingType.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat))),
                        IdentifierName("GetMethod")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(LiteralExpression(
                                SyntaxKind.StringLiteralExpression,
                                Literal(methodSymbol.Name))))));
        var attributeExpression = attributes.ToObjectCreationExpression();
        var actionName = Identifier("actionModel");

        var actionModelDeclaration = VariableDeclaration(
                IdentifierName("var"))
            .WithVariables(SingletonSeparatedList(VariableDeclarator(actionName)
                .WithInitializer(
                    EqualsValueClause(
                        ObjectCreationExpression(actionModel)
                            .WithArgumentList(
                                ArgumentList(
                                    SeparatedList<ArgumentSyntax>(
                                        new SyntaxNodeOrToken[]
                                        {
                                            Argument(methodType), Token(SyntaxKind.CommaToken),
                                            Argument(attributeExpression)
                                        })))))));

        var statement = LocalDeclarationStatement(actionModelDeclaration);
        method = method.AddBodyStatements(statement);
        return statement;
    }

    internal static void AddFilterAttributes(
        LocalDeclarationStatementSyntax actionModel,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var filterMetadataInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata");
        var filterAttributes =
            attributes.Where(attribute => attribute.ImplementsInterface(filterMetadataInterfaceSymbol));

        foreach (var filter in filterAttributes)
        {
            method = method.AddBodyStatements(
                actionModel.AddToList("Filters", filter.ToObjectCreationExpression()));
        }
    }

    internal static void SetActionName(
        LocalDeclarationStatementSyntax actionModel,
        IMethodSymbol methodSymbol,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var actionNameInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ActionNameAttribute");
        var actionNameAttribute =
            attributes.FirstOrDefault(attribute => attribute.ImplementsInterface(actionNameInterfaceSymbol));

        var name = actionNameAttribute?.GetAttributeParameterValue<string>("name") ??
                   CanonicalizeActionName(methodSymbol.Name);

        var memberAccess = MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text),
            IdentifierName("ActionName"));

        var assignment = AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            memberAccess,
            LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Literal(name)));

        method = method.AddBodyStatements(ExpressionStatement(assignment));
    }

    internal static void SetApiDescriptionVisibilityProvider(
        LocalDeclarationStatementSyntax actionModel,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var apiDescriptionProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionProvider");
        var apiVisibilityAttribute = attributes.FirstOrDefault(attribute =>
            attribute.ImplementsInterface(apiDescriptionProviderInterfaceSymbol));

        if (apiVisibilityAttribute is not null)
        {
            var attributeName = CreateUniqueName("apiVisibility");
            var attributeStatement = LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(attributeName))
                    .WithInitializer(EqualsValueClause(apiVisibilityAttribute.ToObjectCreationExpression())))));

            method = method.AddBodyStatements(attributeStatement);
            method = method.AddBodyStatements(
                ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text),
                                IdentifierName("ApiExplorer")),
                            IdentifierName("IsVisible")),
                        PrefixUnaryExpression(
                            SyntaxKind.LogicalNotExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(attributeName),
                                IdentifierName("IgnoreApi"))))));
        }
    }

    internal static void SetApiDescriptionGroupNameProvider(
        LocalDeclarationStatementSyntax actionModel,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var apiGroupNameProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupNameProvider");
        var apiGroupNameAttribute = attributes.FirstOrDefault(attribute =>
            attribute.ImplementsInterface(apiGroupNameProviderInterfaceSymbol));
        if (apiGroupNameAttribute is not null)
        {
            var attributeName = CreateUniqueName("apiGroupName");
            var attributeStatement = LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(attributeName))
                    .WithInitializer(EqualsValueClause(apiGroupNameAttribute.ToObjectCreationExpression())))));
            method = method.AddBodyStatements(attributeStatement);
            method = method.AddBodyStatements(
                ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text),
                                IdentifierName("ApiExplorer")),
                            IdentifierName("GroupName")),
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(attributeName),
                            IdentifierName("GroupName")))));
        }
    }

    internal static void AddRouteValueProviders(
        LocalDeclarationStatementSyntax actionModel,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var routeValueProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IRouteValueProvider");
        var routeValueProviders = attributes
            .Where(attribute => attribute.ImplementsInterface(routeValueProviderInterfaceSymbol))
            .ToList();
        foreach (var routeValueProvider in routeValueProviders)
        {
            var attributeName = CreateUniqueName("routeValueProvider");
            var attributeStatement = LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(attributeName))
                    .WithInitializer(EqualsValueClause(routeValueProvider.ToObjectCreationExpression())))));
            method = method.AddBodyStatements(attributeStatement);
            method = method.AddBodyStatements(
                actionModel.AddToList(
                    "RouteValues",
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(attributeName),
                        IdentifierName("RouteKey")),
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(attributeName),
                        IdentifierName("RouteValue"))));
        }
    }

    internal static void AddSelectors(
        LocalDeclarationStatementSyntax actionModel,
        ICollection<AttributeData> attributes,
        Compilation compilation,
        ref MethodDeclarationSyntax method,
        ref ClassDeclarationSyntax targetClass)
    {
        var actionSelectors = SelectorGenerator.CreateSelectors(attributes, compilation);
        foreach (var selector in actionSelectors)
        {
            targetClass = targetClass.AddMembers(selector);
            method = method.AddBodyStatements(
                actionModel.AddToList(
                    "Selectors",
                    InvocationExpression(IdentifierName(selector.Identifier.Text))
                        .WithArgumentList(ArgumentList())));
        }
    }

    internal static void AddReturn(LocalDeclarationStatementSyntax actionModel, ref MethodDeclarationSyntax method)
    {
        var returnStatement =
            ReturnStatement(IdentifierName(actionModel.Declaration.Variables.First().Identifier.Text));

        method = method.AddBodyStatements(returnStatement);
    }

    private static bool IsAction(IMethodSymbol methodSymbol, Compilation compilation)
    {
        // The equivalent check for IsSpecialName in Roslyn
        if (methodSymbol.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
        {
            return false;
        }

        var nonActionAttributeSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.NonActionAttribute");
        if (methodSymbol.GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass.Equals(nonActionAttributeSymbol, SymbolEqualityComparer.Default)))
        {
            return false;
        }

        // The equivalent check for GetBaseDefinition().DeclaringType == typeof(object) in Roslyn
        var baseDefinition = methodSymbol;
        while (baseDefinition.OverriddenMethod != null)
        {
            baseDefinition = baseDefinition.OverriddenMethod;
        }

        if (baseDefinition.ContainingType.SpecialType == SpecialType.System_Object)
        {
            return false;
        }

        var iDisposableSymbol = compilation.GetTypeByMetadataName("System.IDisposable");
        if (baseDefinition.ContainingType.AllInterfaces.Contains(iDisposableSymbol, SymbolEqualityComparer.Default) &&
            baseDefinition.Name == "Dispose")
        {
            return false;
        }

        if (methodSymbol.IsStatic)
        {
            return false;
        }

        if (methodSymbol.IsAbstract)
        {
            return false;
        }

        if (methodSymbol.MethodKind == MethodKind.Constructor)
        {
            return false;
        }

        if (methodSymbol.IsGenericMethod)
        {
            return false;
        }

        return methodSymbol.DeclaredAccessibility.Equals(Accessibility.Public);
    }

    private static string CanonicalizeActionName(string actionName)
    {
        const string suffix = "Async";

        if (actionName.EndsWith(suffix, StringComparison.Ordinal))
        {
            actionName = actionName.Substring(0, actionName.Length - suffix.Length);
        }

        return actionName;
    }

    private static List<AttributeData> GetRoutingAttributes(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        // For attribute routes on a controller, we want to support 'overriding' routes on a derived
        // class. So we need to walk up the hierarchy looking for the first class to define routes.
        //
        // Then we want to 'filter' the set of attributes, so that only the effective routes apply.
        var routeInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider");
        var routeAttributes = methodSymbol.GetFirstInheritedAttributesImplementingInterface(routeInterfaceSymbol);

        var filteredAttributes = methodSymbol.GetAllAttributesInHierarchy()
            .Where(attribute => !attribute.ImplementsInterface(routeInterfaceSymbol))
            .ToList();

        filteredAttributes.AddRange(routeAttributes);

        return filteredAttributes;
    }

    private static string CreateUniqueName(string prefix)
        => $"{prefix}{Guid.NewGuid():N}";
}
