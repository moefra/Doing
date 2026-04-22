using Microsoft.CodeAnalysis;

namespace Doing.Analyzer;

public sealed class HookMethodSignatureRule
{
    public const string Category = "Usage";

    public HookMethodSignatureRule(
        string diagnosticId,
        string attributeMetadataName,
        string attributeDisplayName,
        string expectedSignature,
        string? returnTypeMetadataName,
        params string[] parameterTypeMetadataNames)
    {
        DiagnosticId = diagnosticId;
        AttributeMetadataName = attributeMetadataName;
        AttributeDisplayName = attributeDisplayName;
        ExpectedSignature = expectedSignature;
        ReturnTypeMetadataName = returnTypeMetadataName;
        ParameterTypeMetadataNames = parameterTypeMetadataNames;
        Descriptor = new DiagnosticDescriptor(
            id: diagnosticId,
            title: $"{attributeDisplayName} methods must use the expected signature",
            messageFormat: "Methods marked with '[{0}]' must have signature '{1}'.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: $"DoingBuild invokes methods marked with [{attributeDisplayName}] via reflection. Their signature must be exactly '{expectedSignature}'.");
    }

    public string DiagnosticId { get; }

    public string AttributeMetadataName { get; }

    public string AttributeDisplayName { get; }

    public string ExpectedSignature { get; }

    public string? ReturnTypeMetadataName { get; }

    public string[] ParameterTypeMetadataNames { get; }

    public DiagnosticDescriptor Descriptor { get; }
}
