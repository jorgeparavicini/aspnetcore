// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.AspNetCore.Mvc.Generators.Extensions;

namespace Microsoft.AspNetCore.Mvc.Extensions;

public class SymbolExtensionsTests
{
    [Fact]
    public void Test_GetAttributesImplementingInterface_Finds_Expected_Attribute()
    {
        var code = @"
        public interface IMyInterface { }

        [MyAttribute]
        public class MyClass { }

        public class MyAttribute : System.Attribute, IMyInterface { }";

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var classDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "MyClass");

        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        var interfaceSymbol = compilation.GetTypeByMetadataName("IMyInterface");

        var attributes = classSymbol.GetAttributesImplementingInterface(interfaceSymbol).ToList();

        Assert.Single(attributes);
        //Assert.Equal("MyAttribute", attributes[0].Name.ToString());
    }

    [Fact]
    public void Test_GetInheritedAttributesImplementingInterface_Finds_Expected_Attribute()
    {
        var code = @"
        public interface IMyInterface { }

        [MyAttribute]
        public class MyBaseClass { }

        public class MyDerivedClass : MyBaseClass { }

        public class MyAttribute : System.Attribute, IMyInterface { }";

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("TestAssembly")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var classDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "MyDerivedClass");

        var interfaceSymbol = compilation.GetTypeByMetadataName("IMyInterface");

        //var attributes = classDeclaration.GetFirstInheritedAttributesImplementingInterface(semanticModel, interfaceSymbol).ToList();

        //Assert.Single(attributes);
        //Assert.Equal("MyAttribute", attributes[0].Name.ToString());
    }
}
