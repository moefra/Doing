using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostBuilderHookSignatureAnalyzer : HookMethodSignatureAnalyzerBase
{
    private static readonly HookMethodSignatureRule Rule = new(
        diagnosticId: "DOING001",
        attributeMetadataName: "Doing.Cli.HostBuilderHookAttribute",
        attributeDisplayName: "HostBuilderHook",
        expectedSignature: "public static void Method(HostApplicationBuilder hostBuilder)",
        returnTypeMetadataName: null,
        parameterTypeMetadataNames: ["Microsoft.Extensions.Hosting.HostApplicationBuilder"]);

    public HostBuilderHookSignatureAnalyzer()
        : base(Rule)
    {
    }
}
