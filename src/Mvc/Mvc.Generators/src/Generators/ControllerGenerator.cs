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

internal static class ControllerGenerator
{
    internal static MethodDeclarationSyntax AddControllerModel(
        INamedTypeSymbol controllerSymbol,
        Compilation compilation,
        ref ClassDeclarationSyntax targetClass)
    {
        var routingAttributes = GetRoutingAttributes(controllerSymbol, compilation);
        var methodName = GetControllerModelMethodName(controllerSymbol);

        var resultMethod = CreateControllerMethod(methodName);

        var controllerModel = AddNewControllerModelStatement(controllerSymbol, routingAttributes, ref resultMethod);

        AddSelectors(routingAttributes, controllerModel, compilation, ref resultMethod, ref targetClass);
        SetControllerName(controllerModel, controllerSymbol, ref resultMethod);
        AddFilterAttributes(routingAttributes, controllerModel, compilation, ref resultMethod);
        AddRouteValueProviders(routingAttributes, controllerModel, compilation, ref resultMethod);
        SetApiDescriptionVisibilityProvider(routingAttributes, controllerModel, compilation, ref resultMethod);
        SetApiDescriptionGroupNameProvider(routingAttributes, controllerModel, compilation, ref resultMethod);
        AddControllerActionFilters(controllerSymbol, controllerModel, compilation, ref resultMethod);
        AddControllerResultFilters(controllerSymbol, controllerModel, compilation, ref resultMethod);
        AddReturnStatement(controllerModel, ref resultMethod);

        targetClass = targetClass.AddMembers(resultMethod);

        return resultMethod;
    }

    internal static MethodDeclarationSyntax CreateControllerMethod(string methodName)
    {
        return MethodDeclaration(
                ParseTypeName("global::Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel"),
                methodName)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));
    }

    internal static LocalDeclarationStatementSyntax AddNewControllerModelStatement(
        ITypeSymbol controllerClassSymbol,
        ICollection<AttributeData> attributes,
        ref MethodDeclarationSyntax method)
    {
        // returns => var controllerModel = new ControllerModel(type, attributes);
        var controllerModel = IdentifierName("global::Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel");

        var controllerModelVariableDeclaration = VariableDeclaration(IdentifierName("var"))
            .WithVariables(
                SingletonSeparatedList(VariableDeclarator(Identifier("controllerModel"))
                    .WithInitializer(
                        EqualsValueClause(
                            ObjectCreationExpression(controllerModel)
                                .WithArgumentList(
                                    ArgumentList(
                                        SeparatedList<ArgumentSyntax>(
                                            new SyntaxNodeOrToken[]
                                            {
                                                Argument(
                                                    InvocationExpression(
                                                        MemberAccessExpression(
                                                            SyntaxKind.SimpleMemberAccessExpression,
                                                            TypeOfExpression(
                                                                IdentifierName(
                                                                    controllerClassSymbol.ToDisplayString(
                                                                        SymbolDisplayFormat.FullyQualifiedFormat))),
                                                            IdentifierName("GetTypeInfo")))),
                                                Token(SyntaxKind.CommaToken),
                                                Argument(attributes.ToObjectCreationExpression())
                                            })))))));

        var statement = LocalDeclarationStatement(controllerModelVariableDeclaration);
        method = method.AddBodyStatements(statement);
        return statement;
    }

    internal static void AddSelectors(
        ICollection<AttributeData> routingAttributes,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method,
        ref ClassDeclarationSyntax targetClass)
    {
        var controllerSelectors = SelectorGenerator.CreateSelectors(routingAttributes, compilation);
        foreach (var selector in controllerSelectors)
        {
            targetClass = targetClass.AddMembers(selector);
            method = method.AddBodyStatements(
                controllerModel.AddToList(
                    "Selectors",
                    InvocationExpression(IdentifierName(selector.Identifier.Text))));
        }
    }

    internal static void SetControllerName(
        LocalDeclarationStatementSyntax controllerModel,
        INamedTypeSymbol controllerSymbol,
        ref MethodDeclarationSyntax method)
    {
        var memberAccess = MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
            IdentifierName("ControllerName"));

        var assignment = AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            memberAccess,
            LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Literal(GetControllerName(controllerSymbol.Name))));

        method = method.AddBodyStatements(ExpressionStatement(assignment));
    }

    internal static void AddFilterAttributes(
        IEnumerable<AttributeData> routingAttributes,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var filterMetadataInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata");
        var filterAttributes =
            routingAttributes.Where(attribute => attribute.ImplementsInterface(filterMetadataInterfaceSymbol));

        foreach (var filter in filterAttributes)
        {
            method = method.AddBodyStatements(
                controllerModel.AddToList("Filters", filter.ToObjectCreationExpression()));
        }
    }

    internal static void AddRouteValueProviders(
        IEnumerable<AttributeData> routingAttributes,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var routeValueProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IRouteValueProvider");
        var routeValueProviders = routingAttributes
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
                controllerModel.AddToList(
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

    internal static void SetApiDescriptionVisibilityProvider(
        IEnumerable<AttributeData> routingAttributes,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var apiDescriptionProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionProvider");
        var apiVisibilityAttribute = routingAttributes.FirstOrDefault(attribute =>
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
                                IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
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
        IEnumerable<AttributeData> routingAttributes,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var apiGroupNameProviderInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupNameProvider");
        var apiGroupNameAttribute = routingAttributes.FirstOrDefault(attribute =>
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
                                IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text),
                                IdentifierName("ApiExplorer")),
                            IdentifierName("GroupName")),
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName(attributeName),
                            IdentifierName("GroupName")))));
        }
    }

    internal static void AddControllerActionFilters(
        INamedTypeSymbol controllerSymbol,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var asyncActionFilterTypeSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter");
        var actionFilterTypeSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IActionFilter");

        if (controllerSymbol.ImplementsInterface(asyncActionFilterTypeSymbol) ||
            controllerSymbol.ImplementsInterface(actionFilterTypeSymbol))
        {
            method = method.AddBodyStatements(
                controllerModel.AddToList(
                    "Filters",
                    ObjectCreationExpression(
                        IdentifierName("global::Microsoft.AspNetCore.Mvc.Filters.ControllerActionFilter"))
                        .WithArgumentList(ArgumentList())));
        }
    }

    internal static void AddControllerResultFilters(
        INamedTypeSymbol controllerSymbol,
        LocalDeclarationStatementSyntax controllerModel,
        Compilation compilation,
        ref MethodDeclarationSyntax method)
    {
        var asyncResultFilterTypeSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IAsyncResultFilter");
        var resultFilterTypeSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Filters.IResultFilter");

        if (controllerSymbol.ImplementsInterface(asyncResultFilterTypeSymbol) ||
            controllerSymbol.ImplementsInterface(resultFilterTypeSymbol))
        {
            method = method.AddBodyStatements(
                controllerModel.AddToList(
                    "Filters",
                    ObjectCreationExpression(
                        IdentifierName("global::Microsoft.AspNetCore.Mvc.Filters.ControllerResultFilter"))
                        .WithArgumentList(ArgumentList())));
        }
    }

    internal static void AddReturnStatement(
        LocalDeclarationStatementSyntax controllerModel,
        ref MethodDeclarationSyntax method)
    {
        var returnStatement = ReturnStatement(
            IdentifierName(controllerModel.Declaration.Variables.First().Identifier.Text));
        method = method.AddBodyStatements(returnStatement);
    }

    private static List<AttributeData> GetRoutingAttributes(
        ITypeSymbol controllerSymbol,
        Compilation compilation)
    {
        // For attribute routes on a controller, we want to support 'overriding' routes on a derived
        // class. So we need to walk up the hierarchy looking for the first class to define routes.
        //
        // Then we want to 'filter' the set of attributes, so that only the effective routes apply.
        var routeInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider");
        var routeAttributes = controllerSymbol.GetFirstInheritedAttributesImplementingInterface(routeInterfaceSymbol);

        var filteredAttributes = controllerSymbol.GetAllAttributesInHierarchy()
            .Where(attribute => !attribute.ImplementsInterface(routeInterfaceSymbol))
            .Where(attribute => !attribute.ToString().Contains("Null"))
            .ToList();

        filteredAttributes.AddRange(routeAttributes);

        return filteredAttributes;
    }

    private static string CreateUniqueName(string prefix)
        => $"{prefix}{Guid.NewGuid():N}";

    private static string GetControllerModelMethodName(ISymbol typeSymbol)
    {
        var qualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            .Replace(".", string.Empty);
        return $"Get{GetControllerName(qualifiedName)}ControllerModel";
    }

    private static string GetControllerName(string name)
    {
        return name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - "Controller".Length)
            : name;
    }
}
