using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.DependencyModel.Resolution;

using Npgsql;

using Squeel.Generators;

using Xunit.Abstractions;

namespace Squeel.Tests.Postgres;

public static class SqueelTestContext
{
    internal static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsPreviewNamespaces", "Squeel")]);

    private static readonly Project BaseProject = CreateProject();

    internal static async Task<(GeneratorRunResult?, Compilation)> Run(
        string cs,
        ITestOutputHelper output,
        string sources,
        params string[] updatedSources)
    {
        var squeelConnectionStringProvider = new SqueelTestAnalyzerConfigOptionsProvider(cs);

        var project = BaseProject
            .AddDocument("TestQuery.cs", SourceText.From($"""
                using System;
                using System.Linq;

                using Npgsql;

                using Squeel;

                using var connection = new NpgsqlConnection("");

                {sources}
                """, Encoding.UTF8))
            .Project;

        var compilation = await project.GetCompilationAsync();

        Assert.NotNull(compilation);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new PostgresGenerator().AsSourceGenerator(),
            ],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true),
            optionsProvider: squeelConnectionStringProvider,
            parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var _);

        foreach (var updatedSource in updatedSources)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(updatedSource, path: $"TestQuery.cs", options: ParseOptions);
            compilation = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.First(), syntaxTree);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out var _);
        }

        var diagnostics = updatedCompilation.GetDiagnostics();

        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));

        var runResults = driver.GetRunResult().Results;

        return (Assert.Single(runResults), updatedCompilation);
    }

    internal static Project CreateProject()
    {
        var projectName = $"TestProject-{Guid.NewGuid()}";
        
        var compilationOptions = new CSharpCompilationOptions(outputKind: OutputKind.ConsoleApplication)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        using var adhoc = new AdhocWorkspace();

        var project = adhoc.CurrentSolution
            .AddProject(projectName, projectName, LanguageNames.CSharp)
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(NpgsqlDataSource).Assembly.Location))
            .WithCompilationOptions(compilationOptions)
            .WithParseOptions(ParseOptions);

        var resolver = new AppLocalResolver();
        var dependencyContext = DependencyContext.Load(typeof(SqueelTestContext).Assembly);

        Assert.NotNull(dependencyContext);

        foreach (var defaultCompileLibrary in dependencyContext.CompileLibraries)
        {
            foreach (var resolveReferencePath in defaultCompileLibrary.ResolveReferencePaths(resolver))
            {
                if (resolveReferencePath.Equals(typeof(PostgresGenerator).Assembly.Location, StringComparison.OrdinalIgnoreCase))
                    continue;

                project = project.AddMetadataReference(MetadataReference.CreateFromFile(resolveReferencePath));
            }
        }

        return project;
    }

    private sealed class AppLocalResolver : ICompilationAssemblyResolver
    {
        public bool TryResolveAssemblyPaths(CompilationLibrary library, List<string>? assemblies)
        {
            foreach (var assembly in library.Assemblies)
            {
                var dll = Path.Combine(Directory.GetCurrentDirectory(), "refs", Path.GetFileName(assembly));
                if (File.Exists(dll))
                {
                    assemblies ??= [];
                    assemblies.Add(dll);
                    return true;
                }

                dll = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileName(assembly));
                if (File.Exists(dll))
                {
                    assemblies ??= [];
                    assemblies.Add(dll);
                    return true;
                }
            }

            return false;
        }
    }
}