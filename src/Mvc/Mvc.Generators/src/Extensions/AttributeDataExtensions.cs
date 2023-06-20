// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.AspNetCore.Mvc.Generators.Extensions;
internal static class AttributeDataExtensions
{
    internal static T? GetAttributeParameterValue<T>(this AttributeData attributeData, string parameterName)
    {
        // Check if attribute has named arguments (Property assignments)
        var namedArgument = attributeData.NamedArguments.SingleOrDefault(na => na.Key.Equals(parameterName, StringComparison.OrdinalIgnoreCase));

        if (!namedArgument.Value.IsNull && namedArgument.Value.Kind != TypedConstantKind.Error)
        {
            return (T)namedArgument.Value.Value!;
        }

        // Check constructor arguments
        // Immutable Array contains no methods or properties for some reason. Because of this we convert it to a list.
        var constructorParameters = attributeData.AttributeConstructor?.Parameters.ToImmutableList();

        if (constructorParameters != null)
        {
            for (var i = 0; i < constructorParameters.Count; i++)
            {
                if (constructorParameters[i].Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    var argument = attributeData.ConstructorArguments[i];
                    if (argument.Kind != TypedConstantKind.Error)
                    {
                        return (T)argument.Value;
                    }
                }
            }
        }

        return default;
    }

    internal static bool IsEquivalent(this AttributeData source, AttributeData target)
    {
        // Compare attribute types
        if (!SymbolEqualityComparer.Default.Equals(source.AttributeClass, target.AttributeClass))
        {
            return false;
        }

        // Compare attribute constructors
        if (!SymbolEqualityComparer.Default.Equals(source.AttributeConstructor, target.AttributeConstructor))
        {
            return false;
        }

        // Compare constructor arguments
        if (!source.ConstructorArguments.SequenceEqual(target.ConstructorArguments))
        {
            return false;
        }

        // Compare named arguments
        if (!source.NamedArguments.SequenceEqual(target.NamedArguments))
        {
            return false;
        }

        // All checks passed, the attributes are equivalent
        return true;
    }
}
