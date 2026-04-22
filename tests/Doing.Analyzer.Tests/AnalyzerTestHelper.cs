using System.Collections.Immutable;
using Autofac;
using Doing.Cli;
using Doing.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Doing.Analyzer.Tests;

internal static class AnalyzerTestHelper
{
    private static readonly ImmutableArray<MetadataReference> MetadataReferences = CreateMetadataReferences();

    public static string BuildDoingBuildSource(string methodSource)
    {
        return $$"""
                 #nullable enable
                 using Autofac;
                 using Doing.Cli;
                 using Doing.Cli.Generator;
                 using Doing.Core;
                 using Microsoft.Extensions.Hosting;
                 using System.CommandLine;
                 using System.CommandLine.Parsing;

                 public class TestBuild() : DoingBuild(null!,null!)
                 {
                     {{methodSource}}
                 }
                 """;
    }

    public static string BuildNonDoingBuildSource(string methodSource)
    {
        return $$"""
                 #nullable enable
                 using Autofac;
                 using Doing.Cli;
                 using Doing.Cli.Generator;
                 using Doing.Core;
                 using Microsoft.Extensions.Hosting;
                 using System.CommandLine;
                 using System.CommandLine.Parsing;

                 public class PlainType
                 {
                     {{methodSource}}
                 }
                 """;
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source, DiagnosticAnalyzer analyzer)
    {
        CSharpCompilation compilation = CreateCompilation(source);

        ImmutableArray<Diagnostic> compileErrors = compilation.GetDiagnostics()
                                                               .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                                                               .ToImmutableArray();

        if (!compileErrors.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "Test source failed to compile:" + Environment.NewLine + string.Join(Environment.NewLine,compileErrors));
        }

        return (await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync())
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: "Doing.Analyzer.Tests.Dynamic",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source,new CSharpParseOptions(LanguageVersion.Preview))],
            references: MetadataReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        string trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

        Dictionary<string, string> referencePaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            referencePaths.TryAdd(Path.GetFileNameWithoutExtension(path),path);
        }

        foreach (string path in new[]
                 {
                     typeof(object).Assembly.Location,
                     typeof(DoingBuild).Assembly.Location,
                     typeof(BuildingOptions).Assembly.Location,
                     typeof(ParsedBuildingOptions).Assembly.Location,
                     typeof(ContainerBuilder).Assembly.Location,
                     typeof(RootCommand).Assembly.Location,
                     typeof(ParseResult).Assembly.Location,
                     typeof(HostApplicationBuilder).Assembly.Location,
                     typeof(DiagnosticAnalyzer).Assembly.Location,
                 })
        {
            referencePaths.TryAdd(Path.GetFileNameWithoutExtension(path),path);
        }

        return [.. referencePaths.Values.Select(static path => MetadataReference.CreateFromFile(path))];
    }
}
