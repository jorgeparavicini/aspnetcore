// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.Mvc.Generators.Extensions;
internal static class SyntaxNodeExtensions
{
    public static bool IsControllerNode(this SyntaxNode node)
    {
        // Accept only classes
        if (node is not ClassDeclarationSyntax classNode)
        {
            return false;
        }

        // The class must be public
        if (!classNode.IsPublic())
        {
            return false;
        }

        // The class must not be abstract
        if (classNode.IsAbstract())
        {
            return false;
        }

        // The class must inherit from ControllerBase or Controller
        return classNode.BaseList?.Types.Any(x =>
            x.Type.ToString() == "ControllerBase" || x.Type.ToString() == "Controller") ?? false;
    }

    private static bool IsPublic(this MemberDeclarationSyntax node)
    {
        return node.Modifiers.Any(x => x.ToString() == "public");
    }

    private static bool IsAbstract(this MemberDeclarationSyntax node)
    {
        return node.Modifiers.Any(x => x.ToString() == "abstract");
    }
}
