using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Doing.Generator.Tests;

internal static class GeneratorTestHelper
{
    private static readonly ImmutableArray<MetadataReference> MetadataReferences = CreateMetadataReferences();

    public static GeneratorTestResult RunGenerator(string source)
    {
        CSharpCompilation compilation = CreateCompilation(source);

        ISourceGenerator generator = new CollectTargetsInfoSourceGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation,out Compilation updatedCompilation,out ImmutableArray<Diagnostic> outputDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        ImmutableArray<Diagnostic> generatorDiagnostics = outputDiagnostics
            .AddRange(runResult.Diagnostics)
            .AddRange(runResult.Results.SelectMany(static result => result.Diagnostics))
            .GroupBy(static diagnostic => (diagnostic.Id, diagnostic.Location.SourceSpan, diagnostic.GetMessage()), DiagnosticKeyComparer.Instance)
            .Select(static diagnostics => diagnostics.First())
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();

        return new GeneratorTestResult(updatedCompilation,generatorDiagnostics,runResult.GeneratedTrees);
    }

    public static string BuildSource(string typeDeclaration)
    {
        return $$"""
                 #nullable enable
                 using System.Collections.Generic;
                 using Doing.Cli.Generator;
                 using Doing.Core;

                 namespace TestCases;

                 {{typeDeclaration}}
                 """;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: $"Doing.Generator.Tests.Dynamic.{Guid.NewGuid():N}",
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
                     typeof(Enumerable).Assembly.Location,
                     typeof(global::Doing.Cli.Generator.ICollectedTargetsInfo).Assembly.Location,
                     typeof(global::Doing.Core.Target).Assembly.Location,
                     typeof(ImmutableDictionary<,>).Assembly.Location,
                     typeof(CSharpCompilation).Assembly.Location,
                 })
        {
            referencePaths.TryAdd(Path.GetFileNameWithoutExtension(path),path);
        }

        return [.. referencePaths.Values.Select(static path => MetadataReference.CreateFromFile(path))];
    }
}

internal sealed class DiagnosticKeyComparer : IEqualityComparer<(string Id, TextSpan Span, string Message)>
{
    public static DiagnosticKeyComparer Instance { get; } = new();

    public bool Equals((string Id, TextSpan Span, string Message) x,(string Id, TextSpan Span, string Message) y)
    {
        return string.Equals(x.Id,y.Id,StringComparison.Ordinal)
               && x.Span.Equals(y.Span)
               && string.Equals(x.Message,y.Message,StringComparison.Ordinal);
    }

    public int GetHashCode((string Id, TextSpan Span, string Message) obj)
    {
        unchecked
        {
            int hashCode = StringComparer.Ordinal.GetHashCode(obj.Id);
            hashCode = (hashCode * 397) ^ obj.Span.GetHashCode();
            hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(obj.Message);
            return hashCode;
        }
    }
}

internal sealed class GeneratorTestResult
{
    public GeneratorTestResult(
        Compilation updatedCompilation,
        ImmutableArray<Diagnostic> generatorDiagnostics,
        ImmutableArray<SyntaxTree> generatedTrees)
    {
        UpdatedCompilation = updatedCompilation;
        GeneratorDiagnostics = generatorDiagnostics;
        GeneratedTrees = generatedTrees;
    }

    public Compilation UpdatedCompilation { get; }

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    public ImmutableArray<SyntaxTree> GeneratedTrees { get; }

    public ImmutableArray<Diagnostic> GetCompilationErrors()
    {
        return UpdatedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    public System.Reflection.Assembly EmitToAssembly()
    {
        using MemoryStream stream = new();
        EmitResult emitResult = UpdatedCompilation.Emit(stream);

        ImmutableArray<Diagnostic> errors = emitResult.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        if (!emitResult.Success || !errors.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "Generated compilation failed to emit:" + Environment.NewLine + string.Join(Environment.NewLine,errors));
        }

        stream.Position = 0;
        return System.Reflection.Assembly.Load(stream.ToArray());
    }
}
