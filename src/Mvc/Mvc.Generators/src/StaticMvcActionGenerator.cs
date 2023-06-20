// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc.Generators.Extensions;
using Microsoft.AspNetCore.Mvc.Generators.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.AspNetCore.Mvc.Generators;

[Generator(LanguageNames.CSharp)]
internal class StaticMvcActionsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(ClassDeclarationSyntax, SemanticModel)> controllerDeclarations =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node.IsControllerNode(),
                transform: static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node, ctx.SemanticModel));

        IncrementalValueProvider<(Compilation, ImmutableArray<(ClassDeclarationSyntax, SemanticModel)>)>
            compilationAndControllers =
                context.CompilationProvider.Combine(controllerDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndControllers,
            static (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<(ClassDeclarationSyntax, SemanticModel)> controllers,
        SourceProductionContext context)
    {
        var applicationModelProviders = new List<CompilationUnitSyntax>();
        var order = 0;

        foreach (var (controllerSyntax, semanticModel) in controllers)
        {
            var compilationUnit = ApplicationModelProviderGenerator.CreateApplicationModelProvider(
                controllerSyntax, semanticModel, compilation, order);

            if (compilationUnit is null)
            {
                continue;
            }

            applicationModelProviders.Add(compilationUnit);
            context.AddSource($"{controllerSyntax.Identifier.Text}.ApplicationModelProvider.g.cs",
                compilationUnit.NormalizeWhitespace().ToFullString());

            order += 1;
        }

        var compilationUnitSyntax =
            MvcCoreServiceCollectionExtensionGenerator.CreateMvcCoreServiceCollectionExtension(
                applicationModelProviders);

        context.AddSource("StaticMvcActions.g.cs", compilationUnitSyntax.NormalizeWhitespace().ToFullString());
    }
}
