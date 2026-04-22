using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Doing.Analyzer;

public abstract class HookMethodSignatureAnalyzerBase : DiagnosticAnalyzer
{
    private const string DoingBuildMetadataName = "DoingBuild";
    private readonly HookMethodSignatureRule _rule;

    protected HookMethodSignatureAnalyzerBase(HookMethodSignatureRule rule)
    {
        _rule = rule;
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_rule.Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            INamedTypeSymbol? doingBuildType = startContext.Compilation.GetTypeByMetadataName(DoingBuildMetadataName);
            INamedTypeSymbol? attributeType = startContext.Compilation.GetTypeByMetadataName(_rule.AttributeMetadataName);

            if (doingBuildType is null || attributeType is null)
            {
                return;
            }

            startContext.RegisterSymbolAction(
                context => AnalyzeMethod(
                    context,
                    doingBuildType,
                    attributeType,
                    _rule),
                SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol doingBuildType,
        INamedTypeSymbol attributeType,
        HookMethodSignatureRule rule)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;

        if (method.MethodKind != MethodKind.Ordinary || method.IsImplicitlyDeclared)
        {
            return;
        }

        if (!method.Locations.Any(static location => location.IsInSource))
        {
            return;
        }

        if (!method.GetAttributes()
                   .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,attributeType)))
        {
            return;
        }

        if (!IsDoingBuildType(method.ContainingType,doingBuildType))
        {
            return;
        }

        if (HasExpectedSignature(method,rule))
        {
            return;
        }

        Location diagnosticLocation = method.Locations.First(static location => location.IsInSource);
        context.ReportDiagnostic(Diagnostic.Create(
            rule.Descriptor,
            diagnosticLocation,
            rule.AttributeDisplayName,
            rule.ExpectedSignature));
    }

    private static bool HasExpectedSignature(IMethodSymbol method, HookMethodSignatureRule rule)
    {
        if (method.DeclaredAccessibility != Accessibility.Public || !method.IsStatic || method.IsGenericMethod)
        {
            return false;
        }

        if (!ReturnTypeMatches(method,rule.ReturnTypeMetadataName))
        {
            return false;
        }

        if (method.Parameters.Length != rule.ParameterTypeMetadataNames.Length)
        {
            return false;
        }

        for (int index = 0; index < method.Parameters.Length; index++)
        {
            IParameterSymbol parameter = method.Parameters[index];
            if (parameter.RefKind != RefKind.None || parameter.IsParams)
            {
                return false;
            }

            if (!TypeMatches(parameter.Type,rule.ParameterTypeMetadataNames[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReturnTypeMatches(IMethodSymbol method, string? expectedReturnTypeMetadataName)
    {
        if (expectedReturnTypeMetadataName is null)
        {
            return method.ReturnsVoid;
        }

        return TypeMatches(method.ReturnType,expectedReturnTypeMetadataName);
    }

    private static bool IsDoingBuildType(INamedTypeSymbol? currentType, INamedTypeSymbol doingBuildType)
    {
        for (INamedTypeSymbol? type = currentType; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type,doingBuildType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeMatches(ITypeSymbol type, string expectedMetadataName)
    {
        string? metadataName = GetMetadataName(type);
        if (!string.Equals(metadataName,expectedMetadataName,StringComparison.Ordinal))
        {
            return false;
        }

        return !type.IsReferenceType || type.NullableAnnotation != NullableAnnotation.Annotated;
    }

    private static string? GetMetadataName(ITypeSymbol type)
    {
        return type switch
        {
            INamedTypeSymbol namedType => GetMetadataName(namedType),
            _ => null,
        };
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        Stack<string> parts = new();

        for (ISymbol? current = type.OriginalDefinition; current is not null;)
        {
            switch (current)
            {
                case INamespaceSymbol { IsGlobalNamespace: true }:
                    current = null;
                    continue;
                case INamespaceSymbol namespaceSymbol:
                    parts.Push(namespaceSymbol.Name);
                    break;
                default:
                    parts.Push(current.MetadataName);
                    break;
            }

            current = current.ContainingSymbol;
        }

        return string.Join(".",parts);
    }
}
