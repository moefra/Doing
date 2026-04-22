using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BuildingOptionsHookSignatureAnalyzer : HookMethodSignatureAnalyzerBase
{
    private static readonly HookMethodSignatureRule Rule = new(
        diagnosticId: "DOING004",
        attributeMetadataName: "Doing.Cli.BuildingOptionsHookAttribute",
        attributeDisplayName: "BuildingOptionsHook",
        expectedSignature: "public static ParsedBuildingOptions Method(ParsedBuildingOptions options, ParseResult parseResult)",
        returnTypeMetadataName: "Doing.Core.ParsedBuildingOptions",
        parameterTypeMetadataNames: ["Doing.Core.ParsedBuildingOptions","System.CommandLine.ParseResult"]);

    public BuildingOptionsHookSignatureAnalyzer()
        : base(Rule)
    {
    }
}
