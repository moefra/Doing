using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostDIHookSignatureAnalyzer : HookMethodSignatureAnalyzerBase
{
    private static readonly HookMethodSignatureRule Rule = new(
        diagnosticId: "DOING002",
        attributeMetadataName: "Doing.Cli.HostDIHookAttribute",
        attributeDisplayName: "HostDIHook",
        expectedSignature: "public static void Method(ContainerBuilder builder)",
        returnTypeMetadataName: null,
        parameterTypeMetadataNames: ["Autofac.ContainerBuilder"]);

    public HostDIHookSignatureAnalyzer()
        : base(Rule)
    {
    }
}
