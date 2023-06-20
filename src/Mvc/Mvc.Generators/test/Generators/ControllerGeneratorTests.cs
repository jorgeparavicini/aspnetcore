// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Generators.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.AspNetCore.Mvc.Generators;

public class ControllerGeneratorTests
{
    [Fact]
    public void Test()
    {
        string sourceCode = @"
using Microsoft.AspNetCore.Mvc;

public class TestController : ControllerBase
{
    [Route(""template"", Name = ""Name"")]
    public void TestAction() { }
}
";

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: new[]
            {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ControllerBase).Assembly.Location)
            });

        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var routeAttributeSyntax = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .First();

        var attributeData = semanticModel.GetDeclaredSymbol(routeAttributeSyntax.Parent.Parent).GetAttributes().First();

        //var result = ControllerGenerator.CreateSelectorModel(new(null, null, null, attributeData), new List<AttributeData>(), compilation);
        //Assert.NotNull(result);
    }
}
