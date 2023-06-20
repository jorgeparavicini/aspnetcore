// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.Mvc.Generators.Extensions;
internal static class AttributeArgumentListSyntaxExtensions
{
    internal static ArgumentListSyntax GetArgumentList(this AttributeArgumentListSyntax syntax)
    {
        var args = syntax.Arguments
            .Where(arg => arg.NameEquals is null)
            .Select(arg => SyntaxFactory.Argument(arg.Expression))
            .ToArray();

        return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args));
    }

    internal static InitializerExpressionSyntax GetInitializerExpression(this AttributeArgumentListSyntax syntax)
    {
        var args = syntax.Arguments
            .Where(arg => arg.NameEquals is not null)
            .Select(arg => SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                arg.NameEquals.Name,
                arg.Expression));

        return SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression, SyntaxFactory.SeparatedList<ExpressionSyntax>(args));
    }
}
