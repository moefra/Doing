using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BuildingDIHookSignatureAnalyzer : HookMethodSignatureAnalyzerBase
{
    private static readonly HookMethodSignatureRule Rule = new(
        diagnosticId: "DOING005",
        attributeMetadataName: "Doing.Cli.Generator.BuildingDIHookAttribute",
        attributeDisplayName: "BuildingDIHook",
        expectedSignature: "public static void Method(ContainerBuilder builder)",
        returnTypeMetadataName: null,
        parameterTypeMetadataNames: ["Autofac.ContainerBuilder"]);

    public BuildingDIHookSignatureAnalyzer()
        : base(Rule)
    {
    }
}
