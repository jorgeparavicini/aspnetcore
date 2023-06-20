// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.AspNetCore.Mvc.Generators.Extensions;

internal static class SymbolExtensions
{
    internal static bool ImplementsInterface(this INamedTypeSymbol typeSymbol, INamedTypeSymbol interfaceSymbol)
        => typeSymbol.AllInterfaces.Any(
            @interface => SymbolEqualityComparer.Default.Equals(@interface, interfaceSymbol));

    internal static bool ImplementsInterface(this AttributeData attributeData, INamedTypeSymbol interfaceSymbol)
        => attributeData.AttributeClass is not null
           && attributeData.AttributeClass.AllInterfaces
               .Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, interfaceSymbol));

    internal static IEnumerable<AttributeData> GetAttributesImplementingInterface(
        this ISymbol classSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        foreach (var attributeData in classSymbol.GetAttributes())
        {
            if (attributeData.ImplementsInterface(interfaceSymbol))
            {
                yield return attributeData;
            }
        }
    }

    internal static ICollection<AttributeData> GetFirstInheritedAttributesImplementingInterface(
        this ITypeSymbol currentClassSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        do
        {
            var attributes = currentClassSymbol.GetAttributesImplementingInterface(interfaceSymbol).ToList();
            if (attributes.Count != 0)
            {
                return attributes;
            }

            currentClassSymbol = currentClassSymbol.BaseType;

            if (currentClassSymbol is null || currentClassSymbol.TypeKind == TypeKind.Error)
            {
                break;
            }

            if (currentClassSymbol.SpecialType == SpecialType.System_Object)
            {
                break;
            }
        } while (true);

        return new List<AttributeData>();
    }

    internal static ICollection<AttributeData> GetFirstInheritedAttributesImplementingInterface(
        this IMethodSymbol currentMethodSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        do
        {
            var attributes = currentMethodSymbol.GetAttributesImplementingInterface(interfaceSymbol).ToList();
            if (attributes.Count != 0)
            {
                return attributes;
            }

            currentMethodSymbol = currentMethodSymbol.OverriddenMethod;

            if (currentMethodSymbol is null)
            {
                break;
            }
        } while (true);

        return new List<AttributeData>();
    }

    internal static IEnumerable<AttributeData> GetAllAttributesInHierarchy(this ITypeSymbol typeSymbol)
    {
        do
        {
            foreach (var attribute in typeSymbol.GetAttributes())
            {
                yield return attribute;
            }

            typeSymbol = typeSymbol.BaseType;
        } while (typeSymbol is not null);
    }

    internal static IEnumerable<AttributeData> GetAllAttributesInHierarchy(this IMethodSymbol methodSymbol)
    {
        do
        {
            foreach (var attribute in methodSymbol.GetAttributes())
            {
                yield return attribute;
            }

            methodSymbol = methodSymbol.OverriddenMethod;
        } while (methodSymbol is not null);
    }
}
