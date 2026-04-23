using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Doing.Generator.Tests;

public class CollectTargetsInfoSourceGeneratorTests
{
    [Test]
    public async Task GeneratesInterfaceImplementationForExpressionBodiedProperties()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);

                public UnnamedTarget New() => new() { Source = _set };

                public Target Foo => New().Name("Foo").Description("Foo description");

                public Target Bar => this.New().Name("Bar").Description("Bar description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        await Assert.That(result.GeneratorDiagnostics).IsEmpty();
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        Type buildScriptType = assembly.GetType("TestCases.BuildScript")
                               ?? throw new InvalidOperationException("Failed to find generated type.");

        ImmutableDictionary<string, string> collectedTargetsInfo = GetCollectedTargets(buildScriptType);

        await Assert.That(collectedTargetsInfo["Foo"]).IsEqualTo("Foo description");
        await Assert.That(collectedTargetsInfo["Bar"]).IsEqualTo("Bar description");
    }

    [Test]
    public async Task CollectsFieldAndPropertyInitializers()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private static readonly TaskSet Set = new([]);

                public static UnnamedTarget New() => new() { Source = Set };

                public Target Alpha = New().Name("Alpha").Description("Alpha description");

                public Target Beta { get; } = New().Name("Beta").Description("Beta description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        await Assert.That(result.GeneratorDiagnostics).IsEmpty();
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Keys.Order(StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo(["Alpha","Beta"]);
    }

    [Test]
    public async Task CollectsCompileTimeConstantNames()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);
                private const string ConstName = "ConstTarget";

                public UnnamedTarget New() => new() { Source = _set };

                public Target ConstTarget => New().Name(ConstName).Description("Const description");

                public Target NameofTarget => New().Name(nameof(NameofTarget)).Description(nameof(BuildScript));
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        await Assert.That(result.GeneratorDiagnostics).IsEmpty();
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(2);
        await Assert.That(info["ConstTarget"]).IsEqualTo("Const description");
        await Assert.That(info["NameofTarget"]).IsEqualTo("BuildScript");
    }

    [Test]
    public async Task CollectsTargetsWhenDescriptionIsFollowedByTargetFluentCalls()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);

                public UnnamedTarget New() => new() { Source = _set };

                public Target BuildDotnet => New().Name(nameof(BuildDotnet))
                                                  .Description("build the dotnet part of doing")
                                                  .Executes(() => {});

                public Target BuildManaged => New().Name(nameof(BuildManaged))
                                                   .Description("build the managed code part of doing")
                                                   .DependsOn(BuildDotnet);
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        await Assert.That(result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Id == "DOING008")).IsEmpty();
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(2);
        await Assert.That(info["BuildDotnet"]).IsEqualTo("build the dotnet part of doing");
        await Assert.That(info["BuildManaged"]).IsEqualTo("build the managed code part of doing");
    }

    [Test]
    public async Task ReportsErrorForNonPartialTypes()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public class BuildScript
            {
                private readonly TaskSet _set = new([]);

                public UnnamedTarget New() => new() { Source = _set };

                public Target Foo => New().Name("Foo").Description("Foo description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);

        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(static diagnostic => diagnostic.Id == "DOING006");

        await Assert.That(diagnostic.Id).IsEqualTo("DOING006");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(result.GeneratedTrees).IsEmpty();
    }

    [Test]
    public async Task ReportsWarningForDuplicateNamesAndKeepsFirstEntry()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);

                public UnnamedTarget New() => new() { Source = _set };

                public Target First => New().Name("Shared").Description("First description");

                public Target Second => New().Name("Shared").Description("Second description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(static diagnostic => diagnostic.Id == "DOING007");

        await Assert.That(diagnostic.Id).IsEqualTo("DOING007");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(1);
        await Assert.That(info["Shared"]).IsEqualTo("First description");
    }

    [Test]
    public async Task ReportsWarningForNonConstantNameAndSkipsTarget()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);
                private readonly string _dynamicName = "Ignored";

                public UnnamedTarget New() => new() { Source = _set };

                public Target Ignored => New().Name(_dynamicName).Description("Ignored description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(static diagnostic => diagnostic.Id == "DOING008");

        await Assert.That(diagnostic.Id).IsEqualTo("DOING008");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).Contains("Ignored");
        await Assert.That(diagnostic.GetMessage()).Contains("Name(...) argument is not a compile-time constant string");
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReportsWarningForNonConstantDescriptionAndSkipsTarget()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);
                private readonly string _description = "Ignored description";

                public UnnamedTarget New() => new() { Source = _set };

                public Target Ignored => New().Name("Ignored").Description(_description);
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(static diagnostic => diagnostic.Id == "DOING008");

        await Assert.That(diagnostic.Id).IsEqualTo("DOING008");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).Contains("Ignored");
        await Assert.That(diagnostic.GetMessage()).Contains("Description(...) argument is not a compile-time constant string");
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReportsWarningForUnsupportedNewChainAndSkipsTarget()
    {
        string source = GeneratorTestHelper.BuildSource(
            """
            [CollectTargetsInfo]
            public partial class BuildScript
            {
                private readonly TaskSet _set = new([]);

                public UnnamedTarget New() => new() { Source = _set };

                public UnnamedTarget Create() => new() { Source = _set };

                public Target Ignored => Create().Name("Ignored").Description("Ignored description");
            }
            """);

        GeneratorTestResult result = GeneratorTestHelper.RunGenerator(source);
        Assembly assembly = result.EmitToAssembly();

        Diagnostic diagnostic = result.GeneratorDiagnostics.Single(static diagnostic => diagnostic.Id == "DOING008");

        await Assert.That(diagnostic.Id).IsEqualTo("DOING008");
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(diagnostic.GetMessage()).Contains("Ignored");
        await Assert.That(diagnostic.GetMessage()).Contains("must start from New() or this.New()");
        await Assert.That(result.GetCompilationErrors()).IsEmpty();

        ImmutableDictionary<string, string> info = GetCollectedTargets(
            assembly.GetType("TestCases.BuildScript")
            ?? throw new InvalidOperationException("Failed to find generated type."));

        await Assert.That(info.Count).IsEqualTo(0);
    }

    private static ImmutableDictionary<string, string> GetCollectedTargets(Type buildScriptType)
    {
        PropertyInfo property = buildScriptType.GetProperty(
                                    "TargetsNameToDescription",
                                    BindingFlags.Public | BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to find generated property.");

        return (ImmutableDictionary<string, string>)(property.GetValue(null)
               ?? throw new InvalidOperationException("Generated property returned null."));
    }
}
