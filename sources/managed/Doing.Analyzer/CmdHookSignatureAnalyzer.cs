using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CmdHookSignatureAnalyzer : HookMethodSignatureAnalyzerBase
{
    private static readonly HookMethodSignatureRule Rule = new(
        diagnosticId: "DOING003",
        attributeMetadataName: "Doing.Cli.CmdHookAttribute",
        attributeDisplayName: "CmdHook",
        expectedSignature: "public static void Method(RootCommand rootCommand)",
        returnTypeMetadataName: null,
        parameterTypeMetadataNames: ["System.CommandLine.RootCommand"]);

    public CmdHookSignatureAnalyzer()
        : base(Rule)
    {
    }
}
