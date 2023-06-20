// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Generators.Extensions;
using Microsoft.AspNetCore.Mvc.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.AspNetCore.Mvc.Generators.Generators;

internal static class SelectorGenerator
{
    internal static IEnumerable<MethodDeclarationSyntax> CreateSelectors(
        ICollection<AttributeData> attributes,
        Compilation compilation)
    {
        // Route attributes create multiple selector models, we want to split the set of
        // attributes based on these so each selector only has the attributes that affect it.
        //
        // The set of route attributes are split into those that 'define' a route versus those that are
        // 'silent'.
        //
        // We need to define a selector for each attribute that 'defines' a route, and a single selector
        // for all of the ones that don't (if any exist).
        //
        // If the attribute that 'defines' a route is NOT an IActionHttpMethodProvider, then we'll include with
        // it, any IActionHttpMethodProvider that are 'silent' IRouteTemplateProviders. In this case the 'extra'
        // action for silent route providers isn't needed.
        //
        // Ex:
        // [HttpGet]
        // [AcceptVerbs("POST", "PUT")]
        // [HttpPost("Api/Things")]
        // public void DoThing()
        //
        // This will generate 2 selectors:
        // 1. [HttpPost("Api/Things")]
        // 2. [HttpGet], [AcceptVerbs("POST", "PUT")]
        //
        // Another example of this situation is:
        //
        // [Route("api/Products")]
        // [AcceptVerbs("GET", "HEAD")]
        // [HttpPost("api/Products/new")]
        //
        // This will generate 2 selectors:
        // 1. [AcceptVerbs("GET", "HEAD")]
        // 2. [HttpPost]
        //
        // Note that having a route attribute that doesn't define a route template _might_ be an error. We
        // don't have enough context to really know at this point so we just pass it on.
        var routeInterfaceSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider");
        var actionHttpMethodProviderSymbol =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.Routing.IActionHttpMethodProvider");

        var routeProviders = new List<RouteTemplateProvider>();
        var createSelectorForSilentRouteProviders = false;

        foreach (var attribute in attributes)
        {
            if (!attribute.ImplementsInterface(routeInterfaceSymbol))
            {
                continue;
            }

            var templateProvider = GetStaticRouteTemplateProvider(attribute);

            if (templateProvider.IsSilent)
            {
                createSelectorForSilentRouteProviders = true;
            }
            else
            {
                routeProviders.Add(templateProvider);
            }
        }

        foreach (var routeProvider in routeProviders)
        {
            // If we see an attribute like
            // [Route(...)]
            //
            // Then we want to group any attributes like [HttpGet] with it.
            //
            // Basically...
            //
            // [HttpGet]
            // [HttpPost("Products")]
            // public void Foo() { }
            //
            // Is two selectors. And...
            //
            // [HttpGet]
            // [Route("Products")]
            // public void Foo() { }
            //
            // Is one selector.
            if (!routeProvider.Attribute.ImplementsInterface(actionHttpMethodProviderSymbol))
            {
                createSelectorForSilentRouteProviders = false;
                break;
            }
        }

        var selectorModels = new List<MethodDeclarationSyntax>();
        if (routeProviders.Count == 0 && !createSelectorForSilentRouteProviders)
        {
            // Simple case, all attributes apply
            selectorModels.Add(CreateSelectorModel(null, attributes, compilation));
        }
        else
        {
            // Each of these routeProviders are the ones that actually have routing information on them
            // something like [HttpGet] won't show up here, but [HttpGet("Products")] will.
            foreach (var routeProvider in routeProviders)
            {
                var filteredAttributes = new List<AttributeData>();
                foreach (var attribute in attributes)
                {
                    if (attribute.IsEquivalent(routeProvider.Attribute))
                    {
                        filteredAttributes.Add(attribute);
                    }
                    else if (InRouteProviders(routeProviders, attribute))
                    {
                        // Exclude other route template providers
                        // Example:
                        // [HttpGet("template")]
                        // [Route("template/{id}")]
                    }
                    else if (routeProvider.Attribute.ImplementsInterface(actionHttpMethodProviderSymbol)
                             && attribute.ImplementsInterface(actionHttpMethodProviderSymbol))
                    {
                        // Example:
                        // [HttpGet("template")]
                        // [AcceptVerbs("GET", "POST")]
                        //
                        // Exclude other http method providers if this route is an
                        // http method provider.
                    }
                    else
                    {
                        filteredAttributes.Add(attribute);
                    }
                }

                selectorModels.Add(CreateSelectorModel(routeProvider, filteredAttributes, compilation));
            }

            if (createSelectorForSilentRouteProviders)
            {
                var filteredAttributes = new List<AttributeData>();
                foreach (var attribute in attributes)
                {
                    if (!InRouteProviders(routeProviders, attribute))
                    {
                        filteredAttributes.Add(attribute);
                    }
                }

                selectorModels.Add(CreateSelectorModel(route: null, attributes: filteredAttributes, compilation));
            }
        }

        return selectorModels;
    }

    internal static MethodDeclarationSyntax CreateSelectorModel(
        RouteTemplateProvider? route,
        ICollection<AttributeData> attributes,
        Compilation compilation)
    {
        var selectorModelIdentifier = ParseTypeName("global::Microsoft.AspNetCore.Mvc.ApplicationModels.SelectorModel");
        var methodName = Identifier(CreateUniqueName("SelectorModel"));

        var methodBlock = MethodDeclaration(selectorModelIdentifier, methodName)
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));

        // var selectorModel{uniqueId} = new SelectorModel();
        var selectorIdentifier = IdentifierName("selectorModel");
        var selectorModelDeclaration = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(selectorIdentifier.Identifier)
                            .WithInitializer(
                                EqualsValueClause(
                                    ObjectCreationExpression(
                                            selectorModelIdentifier)
                                        .WithArgumentList(ArgumentList()))))));

        methodBlock = methodBlock.AddBodyStatements(selectorModelDeclaration);

        if (route is not null)
        {
            var attributeRouteModel = "global::Microsoft.AspNetCore.Mvc.ApplicationModels.AttributeRouteModel";

            var attributeRouteModelAssignment = ExpressionStatement(
                AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        selectorIdentifier,
                        IdentifierName("AttributeRouteModel")),
                    ObjectCreationExpression(
                            IdentifierName(attributeRouteModel))
                        .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(
                            route.Attribute.ToObjectCreationExpression()))))));

            methodBlock = methodBlock.AddBodyStatements(attributeRouteModelAssignment);
        }

        // Add action constraints
        var constraintMetadataSymbol =
            compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Mvc.ActionConstraints.IActionConstraintMetadata");
        var constrainedAttributes = attributes.Where(attr => attr.ImplementsInterface(constraintMetadataSymbol));
        foreach (var constrainedAttribute in constrainedAttributes)
        {
            var addConstrainedAttributeExpression = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            selectorIdentifier,
                            IdentifierName("ActionConstraints")),
                        IdentifierName("Add")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(constrainedAttribute.ToObjectCreationExpression()))));

            methodBlock = methodBlock.AddBodyStatements(ExpressionStatement(addConstrainedAttributeExpression));
        }

        // Endpoint Metadata
        foreach (var attribute in attributes)
        {
            var addConstrainedAttributeExpression = InvocationExpression(
                    MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            selectorIdentifier,
                            IdentifierName("EndpointMetadata")),
                        IdentifierName("Add")))
                .WithArgumentList(
                    ArgumentList(
                        SingletonSeparatedList(
                            Argument(attribute.ToObjectCreationExpression()))));

            methodBlock = methodBlock.AddBodyStatements(ExpressionStatement(addConstrainedAttributeExpression));
        }

        // Apply HTTP method
        var attributeAssignment = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("attributes"))
                    .WithInitializer(EqualsValueClause(attributes.ToObjectCreationExpression())))));

        methodBlock = methodBlock.AddBodyStatements(attributeAssignment);

        const string httpMethodProviderType = "global::Microsoft.AspNetCore.Mvc.Routing.IActionHttpMethodProvider";
        var httpMethodsStatement = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .WithVariables(
                    SingletonSeparatedList(
                        VariableDeclarator(Identifier("httpMethods"))
                            .WithInitializer(EqualsValueClause(
                                InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        InvocationExpression(
                                                MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    InvocationExpression(
                                                            MemberAccessExpression(
                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                InvocationExpression(
                                                                    MemberAccessExpression(
                                                                        SyntaxKind.SimpleMemberAccessExpression,
                                                                        IdentifierName("attributes"),
                                                                        GenericName(Identifier("OfType"))
                                                                            .WithTypeArgumentList(
                                                                                TypeArgumentList(
                                                                                    SingletonSeparatedList<TypeSyntax>(
                                                                                        IdentifierName(
                                                                                            httpMethodProviderType)))))),
                                                                IdentifierName("SelectMany")))
                                                        .WithArgumentList(
                                                            ArgumentList(SingletonSeparatedList(
                                                                Argument(
                                                                    SimpleLambdaExpression(
                                                                            Parameter(Identifier("a")))
                                                                        .WithExpressionBody(
                                                                            MemberAccessExpression(
                                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                                IdentifierName("a"),
                                                                                IdentifierName("HttpMethods"))))))),
                                                    IdentifierName("Distinct")))
                                            .WithArgumentList(
                                                ArgumentList(
                                                    SingletonSeparatedList(
                                                        Argument(
                                                            MemberAccessExpression(
                                                                SyntaxKind.SimpleMemberAccessExpression,
                                                                IdentifierName("global::System.StringComparer"),
                                                                IdentifierName("OrdinalIgnoreCase")))))),
                                        IdentifierName("ToArray"))))))));

        methodBlock = methodBlock.AddBodyStatements(httpMethodsStatement);

        var assignHttpMethods =
                IfStatement(
                    BinaryExpression(
                        SyntaxKind.GreaterThanExpression,
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("httpMethods"),
                            IdentifierName("Length")),
                        LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            Literal(0))),
                    Block(
                        ExpressionStatement(
                            InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("selectorModel"),
                                            IdentifierName("ActionConstraints")),
                                        IdentifierName("Add")))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                ObjectCreationExpression(
                                                        IdentifierName("global::Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint"))
                                                    .WithArgumentList(
                                                        ArgumentList(
                                                            SingletonSeparatedList(
                                                                Argument(
                                                                    IdentifierName("httpMethods")))))))))),
                        ExpressionStatement(
                            InvocationExpression(
                                    MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName("selectorModel"),
                                            IdentifierName("EndpointMetadata")),
                                        IdentifierName("Add")))
                                .WithArgumentList(
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                ObjectCreationExpression(
                                                        IdentifierName("global::Microsoft.AspNetCore.Routing.HttpMethodMetadata"))
                                                    .WithArgumentList(
                                                        ArgumentList(
                                                            SingletonSeparatedList(
                                                                Argument(
                                                                    IdentifierName("httpMethods"))))))))))));

        methodBlock = methodBlock.AddBodyStatements(assignHttpMethods);

        methodBlock = methodBlock.AddBodyStatements(ReturnStatement(IdentifierName("selectorModel")));

        return methodBlock;
    }

    private static RouteTemplateProvider GetStaticRouteTemplateProvider(AttributeData attribute)
    {
        var template = attribute.GetAttributeParameterValue<string>("template");
        var order = attribute.GetAttributeParameterValue<string>("order");
        var name = attribute.GetAttributeParameterValue<string>("name");

        return new(template, order, name, attribute);
    }

    private static bool InRouteProviders(IEnumerable<RouteTemplateProvider> routeProviders, AttributeData attribute)
    {
        return routeProviders.Any(rp => rp.Attribute.IsEquivalent(attribute));
    }

    private static string CreateUniqueName(string prefix)
        => $"{prefix}{Guid.NewGuid():N}";
}
