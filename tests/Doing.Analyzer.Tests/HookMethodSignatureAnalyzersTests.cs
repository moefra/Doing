using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer.Tests;

public class HookMethodSignatureAnalyzersTests
{
    private static readonly AnalyzerCase[] Cases =
    [
        new(
            "DOING001",
            static () => new HostBuilderHookSignatureAnalyzer(),
            """
            [HostBuilderHook]
            public static void ConfigureHost(HostApplicationBuilder hostBuilder)
            {
            }
            """,
            """
            [HostBuilderHook]
            public void ConfigureHost(HostApplicationBuilder hostBuilder)
            {
            }
            """,
            """
            [HostBuilderHook]
            private static void ConfigureHost(HostApplicationBuilder hostBuilder)
            {
            }
            """,
            """
            [HostBuilderHook]
            public static void ConfigureHost()
            {
            }
            """,
            """
            [HostBuilderHook]
            public static void ConfigureHost(ContainerBuilder builder)
            {
            }
            """,
            """
            [HostBuilderHook]
            public static int ConfigureHost(HostApplicationBuilder hostBuilder)
            {
                return 0;
            }
            """),
        new(
            "DOING002",
            static () => new HostDIHookSignatureAnalyzer(),
            """
            [HostDIHook]
            public static void ConfigureServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [HostDIHook]
            public void ConfigureServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [HostDIHook]
            private static void ConfigureServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [HostDIHook]
            public static void ConfigureServices()
            {
            }
            """,
            """
            [HostDIHook]
            public static void ConfigureServices(RootCommand rootCommand)
            {
            }
            """,
            """
            [HostDIHook]
            public static int ConfigureServices(ContainerBuilder builder)
            {
                return 0;
            }
            """),
        new(
            "DOING003",
            static () => new CmdHookSignatureAnalyzer(),
            """
            [CmdHook]
            public static void ConfigureCommand(RootCommand rootCommand)
            {
            }
            """,
            """
            [CmdHook]
            public void ConfigureCommand(RootCommand rootCommand)
            {
            }
            """,
            """
            [CmdHook]
            private static void ConfigureCommand(RootCommand rootCommand)
            {
            }
            """,
            """
            [CmdHook]
            public static void ConfigureCommand()
            {
            }
            """,
            """
            [CmdHook]
            public static void ConfigureCommand(ContainerBuilder builder)
            {
            }
            """,
            """
            [CmdHook]
            public static int ConfigureCommand(RootCommand rootCommand)
            {
                return 0;
            }
            """),
        new(
            "DOING004",
            static () => new BuildingOptionsHookSignatureAnalyzer(),
            """
            [BuildingOptionsHook]
            public static ParsedBuildingOptions ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
                return options;
            }
            """,
            """
            [BuildingOptionsHook]
            public ParsedBuildingOptions ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
                return options;
            }
            """,
            """
            [BuildingOptionsHook]
            private static ParsedBuildingOptions ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
                return options;
            }
            """,
            """
            [BuildingOptionsHook]
            public static ParsedBuildingOptions ConfigureOptions(ParsedBuildingOptions options)
            {
                return options;
            }
            """,
            """
            [BuildingOptionsHook]
            public static ParsedBuildingOptions ConfigureOptions(RootCommand rootCommand, ParseResult parseResult)
            {
                return new ParsedBuildingOptions(false, false);
            }
            """,
            """
            [BuildingOptionsHook]
            public static RootCommand ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
                return new RootCommand();
            }
            """),
        new(
            "DOING005",
            static () => new BuildingDIHookSignatureAnalyzer(),
            """
            [BuildingDIHook]
            public static void ConfigureScopedServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [BuildingDIHook]
            public void ConfigureScopedServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [BuildingDIHook]
            private static void ConfigureScopedServices(ContainerBuilder builder)
            {
            }
            """,
            """
            [BuildingDIHook]
            public static void ConfigureScopedServices()
            {
            }
            """,
            """
            [BuildingDIHook]
            public static void ConfigureScopedServices(RootCommand rootCommand)
            {
            }
            """,
            """
            [BuildingDIHook]
            public static int ConfigureScopedServices(ContainerBuilder builder)
            {
                return 0;
            }
            """),
    ];

    [Test]
    public async Task ValidSignatures_DoNotReportDiagnostics()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics =
                await AnalyzerTestHelper.GetDiagnosticsAsync(
                    AnalyzerTestHelper.BuildDoingBuildSource(testCase.ValidMethod),
                    testCase.CreateAnalyzer());

            if (diagnostics.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Expected no diagnostics for {testCase.DiagnosticId}, but got:{Environment.NewLine}{string.Join(Environment.NewLine,diagnostics)}");
            }
        }
    }

    [Test]
    public async Task MissingStatic_ReportsForEveryHook()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            await AssertSingleDiagnosticAsync(testCase,testCase.MissingStaticMethod);
        }
    }

    [Test]
    public async Task MissingPublic_ReportsForEveryHook()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            await AssertSingleDiagnosticAsync(testCase,testCase.MissingPublicMethod);
        }
    }

    [Test]
    public async Task WrongParameterCount_ReportsForEveryHook()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            await AssertSingleDiagnosticAsync(testCase,testCase.WrongParameterCountMethod);
        }
    }

    [Test]
    public async Task WrongParameterType_ReportsForEveryHook()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            await AssertSingleDiagnosticAsync(testCase,testCase.WrongParameterTypeMethod);
        }
    }

    [Test]
    public async Task WrongReturnType_ReportsForEveryHook()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            await AssertSingleDiagnosticAsync(testCase,testCase.WrongReturnTypeMethod);
        }
    }

    [Test]
    public async Task BuildingOptionsHook_VoidReturn_ReportsDiagnostic()
    {
        AnalyzerCase testCase = Cases.Single(static analyzerCase => analyzerCase.DiagnosticId == "DOING004");

        await AssertSingleDiagnosticAsync(
            testCase,
            """
            [BuildingOptionsHook]
            public static void ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
            }
            """);
    }

    [Test]
    public async Task BuildingOptionsHook_NullableReturn_ReportsDiagnostic()
    {
        AnalyzerCase testCase = Cases.Single(static analyzerCase => analyzerCase.DiagnosticId == "DOING004");

        await AssertSingleDiagnosticAsync(
            testCase,
            """
            [BuildingOptionsHook]
            public static ParsedBuildingOptions? ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
            {
                return options;
            }
            """);
    }

    [Test]
    public async Task BuildingOptionsHook_ReversedParameters_ReportsDiagnostic()
    {
        AnalyzerCase testCase = Cases.Single(static analyzerCase => analyzerCase.DiagnosticId == "DOING004");

        await AssertSingleDiagnosticAsync(
            testCase,
            """
            [BuildingOptionsHook]
            public static ParsedBuildingOptions ConfigureOptions(ParseResult parseResult, ParsedBuildingOptions options)
            {
                return options;
            }
            """);
    }

    [Test]
    public async Task NonDoingBuildTypes_DoNotReportDiagnostics()
    {
        foreach (AnalyzerCase testCase in Cases)
        {
            ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics =
                await AnalyzerTestHelper.GetDiagnosticsAsync(
                    AnalyzerTestHelper.BuildNonDoingBuildSource(testCase.MissingStaticMethod),
                    testCase.CreateAnalyzer());

            if (diagnostics.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Expected no diagnostics for non-DoingBuild case {testCase.DiagnosticId}, but got:{Environment.NewLine}{string.Join(Environment.NewLine,diagnostics)}");
            }
        }
    }

    private static async Task AssertSingleDiagnosticAsync(AnalyzerCase testCase, string methodSource)
    {
        ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics =
            await AnalyzerTestHelper.GetDiagnosticsAsync(
                AnalyzerTestHelper.BuildDoingBuildSource(methodSource),
                testCase.CreateAnalyzer());

        if (diagnostics.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected 1 diagnostic for {testCase.DiagnosticId}, but got {diagnostics.Length}:{Environment.NewLine}{string.Join(Environment.NewLine,diagnostics)}");
        }

        await Assert.That(diagnostics[0].Id).IsEqualTo(testCase.DiagnosticId);
    }

    private sealed record AnalyzerCase(
        string DiagnosticId,
        Func<DiagnosticAnalyzer> CreateAnalyzer,
        string ValidMethod,
        string MissingStaticMethod,
        string MissingPublicMethod,
        string WrongParameterCountMethod,
        string WrongParameterTypeMethod,
        string WrongReturnTypeMethod);
}
