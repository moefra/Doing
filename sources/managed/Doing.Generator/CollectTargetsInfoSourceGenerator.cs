using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Doing.Generator;

/// <summary>
/// Generates collected target metadata for types marked with <c>[CollectTargetsInfo]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CollectTargetsInfoSourceGenerator : IIncrementalGenerator
{
    private const string CollectTargetsInfoAttributeMetadataName = "Doing.Cli.Generator.CollectTargetsInfoAttribute";
    private const string TargetMetadataName = "Doing.Core.Target";
    private const string UnnamedTargetMetadataName = "Doing.Core.UnnamedTarget";
    private const string UndescriptedTargetMetadataName = "Doing.Core.UndescriptedTarget";

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
        | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TypeAnalysisResult> candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CollectTargetsInfoAttributeMetadataName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, cancellationToken) => AnalyzeType(attributeContext,cancellationToken));

        context.RegisterSourceOutput(candidates.Collect(), static (productionContext, results) =>
        {
            HashSet<INamedTypeSymbol> seenTypes = new(SymbolEqualityComparer.Default);

            foreach (TypeAnalysisResult result in results)
            {
                if (!seenTypes.Add(result.Type))
                {
                    continue;
                }

                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    productionContext.ReportDiagnostic(diagnostic);
                }

                if (!result.CanGenerateSource)
                {
                    continue;
                }

                productionContext.AddSource(
                    GetHintName(result.Type),
                    GenerateSource(result));
            }
        });
    }

    private static TypeAnalysisResult AnalyzeType(GeneratorAttributeSyntaxContext context,CancellationToken cancellationToken)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        List<Diagnostic> diagnostics = [];

        if (!CanGeneratePartialType(type,out Location? partialRequirementLocation,out string? partialRequirementTypeName,cancellationToken))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.TypeMustBePartial,
                partialRequirementLocation ?? type.Locations.FirstOrDefault(static location => location.IsInSource),
                partialRequirementTypeName ?? type.Name));

            return new TypeAnalysisResult(type,[],diagnostics.ToImmutableArray(),canGenerateSource: false);
        }

        ImmutableArray<CollectedTargetEntry> entries =
            CollectEntries(type,context.SemanticModel.Compilation,cancellationToken,out ImmutableArray<Diagnostic> collectionDiagnostics);

        diagnostics.AddRange(collectionDiagnostics);

        return new TypeAnalysisResult(type,entries,diagnostics.ToImmutableArray(),canGenerateSource: true);
    }

    private static bool CanGeneratePartialType(
        INamedTypeSymbol type,
        out Location? location,
        out string? typeName,
        CancellationToken cancellationToken)
    {
        foreach (INamedTypeSymbol current in EnumerateContainingTypes(type))
        {
            foreach (SyntaxReference syntaxReference in current.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration)
                {
                    continue;
                }

                if (declaration.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                location = declaration.Identifier.GetLocation();
                typeName = current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                return false;
            }
        }

        location = null;
        typeName = null;
        return true;
    }

    private static ImmutableArray<CollectedTargetEntry> CollectEntries(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken,
        out ImmutableArray<Diagnostic> collectionDiagnostics)
    {
        List<CollectedTargetEntry> collectedEntries = [];
        List<Diagnostic> diagnostics = [];
        HashSet<string> seenTargetNames = new(StringComparer.Ordinal);

        IEnumerable<ISymbol> orderedMembers = type.GetMembers()
            .Where(static member => !member.IsImplicitlyDeclared)
            .Where(static member => member.Locations.Any(static location => location.IsInSource))
            .OrderBy(static member => member.Locations.First(static location => location.IsInSource).SourceTree?.FilePath,StringComparer.Ordinal)
            .ThenBy(static member => member.Locations.First(static location => location.IsInSource).SourceSpan.Start);

        foreach (ISymbol member in orderedMembers)
        {
            if (!IsTargetMember(member))
            {
                continue;
            }

            if (!TryGetInitializationExpression(member,compilation,cancellationToken,out ExpressionSyntax? expression,out SemanticModel? semanticModel))
            {
                continue;
            }

            if (expression is null || semanticModel is null)
            {
                continue;
            }

            ExpressionSyntax actualExpression = expression!;
            SemanticModel actualSemanticModel = semanticModel!;

            if (!TryExtractTargetInfo(actualExpression,actualSemanticModel,cancellationToken,out CollectedTargetEntry entry,out string? failureReason))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnrecognizedTarget,
                    member.Locations.First(static location => location.IsInSource),
                    member.Name,
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    failureReason ?? "the target expression could not be statically analyzed"));
                continue;
            }

            string name = entry.Name;
            string description = entry.Description;

            if (!seenTargetNames.Add(name))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateTargetName,
                    member.Locations.First(static location => location.IsInSource),
                    name,
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                continue;
            }

            collectedEntries.Add(entry);
        }

        collectionDiagnostics = diagnostics.ToImmutableArray();
        return collectedEntries.ToImmutableArray();
    }

    private static bool IsTargetMember(ISymbol member)
    {
        return member switch
        {
            IPropertySymbol propertySymbol => TypeMatches(propertySymbol.Type,TargetMetadataName),
            IFieldSymbol fieldSymbol => TypeMatches(fieldSymbol.Type,TargetMetadataName),
            _ => false,
        };
    }

    private static bool TryGetInitializationExpression(
        ISymbol member,
        Compilation compilation,
        CancellationToken cancellationToken,
        out ExpressionSyntax? expression,
        out SemanticModel? semanticModel)
    {
        SyntaxReference? syntaxReference = member.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
        {
            expression = null;
            semanticModel = null;
            return false;
        }

        SyntaxNode syntax = syntaxReference.GetSyntax(cancellationToken);
        semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);

        switch (syntax)
        {
            case PropertyDeclarationSyntax propertyDeclaration:
                expression = propertyDeclaration.ExpressionBody?.Expression
                             ?? propertyDeclaration.Initializer?.Value;
                return expression is not null;
            case VariableDeclaratorSyntax variableDeclarator:
                expression = variableDeclarator.Initializer?.Value;
                return expression is not null;
            default:
                expression = null;
                semanticModel = null;
                return false;
        }
    }

    private static bool TryExtractTargetInfo(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out CollectedTargetEntry entry,
        out string? failureReason)
    {
        entry = default;
        failureReason = null;

        if (semanticModel.GetOperation(expression,cancellationToken) is not IInvocationOperation rootInvocation)
        {
            failureReason = "the initializer does not contain a supported Description(...) call";
            return false;
        }

        if (!TryFindDescriptionInvocation(rootInvocation,out IInvocationOperation? descriptionInvocation,out failureReason)
            || descriptionInvocation is null)
        {
            failureReason ??= "the initializer does not contain a supported Description(...) call";
            return false;
        }

        if (!IsDescriptionInvocation(descriptionInvocation,out string? description,out failureReason))
        {
            return false;
        }

        if (descriptionInvocation.Instance is not IInvocationOperation nameInvocation)
        {
            failureReason = "the Description(...) receiver is not a supported Name(...) call";
            return false;
        }

        if (!IsNameInvocation(nameInvocation,out string? name,out failureReason))
        {
            return false;
        }

        if (nameInvocation.Instance is not IInvocationOperation newInvocation)
        {
            failureReason = "the Name(...) receiver is not a supported New() call";
            return false;
        }

        if (!IsAllowedNewInvocation(newInvocation))
        {
            failureReason = "the target chain must start from New() or this.New()";
            return false;
        }

        entry = new CollectedTargetEntry(name!,description!);
        return true;
    }

    private static bool TryFindDescriptionInvocation(
        IInvocationOperation rootInvocation,
        out IInvocationOperation? descriptionInvocation,
        out string? failureReason)
    {
        descriptionInvocation = null;
        failureReason = null;

        IInvocationOperation currentInvocation = rootInvocation;
        while (true)
        {
            if (string.Equals(currentInvocation.TargetMethod.Name,"Description",StringComparison.Ordinal))
            {
                descriptionInvocation = currentInvocation;
                return true;
            }

            if (!IsTargetFluentInvocation(currentInvocation)
                || currentInvocation.Instance is not IInvocationOperation receiverInvocation)
            {
                failureReason = "the initializer does not contain a supported Description(...) call";
                return false;
            }

            currentInvocation = receiverInvocation;
        }
    }

    private static bool IsDescriptionInvocation(IInvocationOperation invocation,out string? description,out string? failureReason)
    {
        description = null;
        failureReason = null;

        if (!string.Equals(invocation.TargetMethod.Name,"Description",StringComparison.Ordinal))
        {
            failureReason = "the initializer does not contain a supported Description(...) call";
            return false;
        }

        if (!TypeMatches(invocation.TargetMethod.ContainingType,UndescriptedTargetMetadataName)
            || !TypeMatches(invocation.TargetMethod.ReturnType,TargetMetadataName)
            || invocation.Arguments.Length != 1)
        {
            failureReason = "the Description(...) call does not match the expected Doing target syntax";
            return false;
        }

        if (!TryGetCompileTimeConstantString(invocation.Arguments[0],out description))
        {
            failureReason = "the Description(...) argument is not a compile-time constant string";
            return false;
        }

        return true;
    }

    private static bool IsNameInvocation(IInvocationOperation invocation,out string? name,out string? failureReason)
    {
        name = null;
        failureReason = null;

        if (!string.Equals(invocation.TargetMethod.Name,"Name",StringComparison.Ordinal))
        {
            failureReason = "the Description(...) receiver is not a supported Name(...) call";
            return false;
        }

        if (!TypeMatches(invocation.TargetMethod.ContainingType,UnnamedTargetMetadataName)
            || !TypeMatches(invocation.TargetMethod.ReturnType,UndescriptedTargetMetadataName)
            || invocation.Arguments.Length != 1)
        {
            failureReason = "the Name(...) call does not match the expected Doing target syntax";
            return false;
        }

        if (!TryGetCompileTimeConstantString(invocation.Arguments[0],out name))
        {
            failureReason = "the Name(...) argument is not a compile-time constant string";
            return false;
        }

        return true;
    }

    private static bool IsTargetFluentInvocation(IInvocationOperation invocation)
    {
        return !invocation.TargetMethod.IsStatic
               && invocation.Instance is not null
               && TypeMatches(invocation.TargetMethod.ContainingType,TargetMetadataName)
               && TypeMatches(invocation.TargetMethod.ReturnType,TargetMetadataName);
    }

    private static bool IsAllowedNewInvocation(IInvocationOperation invocation)
    {
        if (!string.Equals(invocation.TargetMethod.Name,"New",StringComparison.Ordinal))
        {
            return false;
        }

        if (!TypeMatches(invocation.TargetMethod.ReturnType,UnnamedTargetMetadataName) || invocation.Arguments.Length != 0)
        {
            return false;
        }

        return invocation.Syntax is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "New" }
        }
        or InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: ThisExpressionSyntax,
                Name.Identifier.ValueText: "New"
            }
        };
    }

    private static bool TryGetCompileTimeConstantString(IArgumentOperation argument,out string? value)
    {
        Optional<object?> constantValue = argument.Value.ConstantValue;
        if (constantValue.HasValue && constantValue.Value is string text)
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TypeMatches(ITypeSymbol? type,string metadataName)
    {
        if (type is null)
        {
            return false;
        }

        return string.Equals(GetMetadataName(type),metadataName,StringComparison.Ordinal);
    }

    private static string? GetMetadataName(ITypeSymbol type)
    {
        return type switch
        {
            INamedTypeSymbol namedTypeSymbol => GetMetadataName(namedTypeSymbol),
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

    private static string GetHintName(INamedTypeSymbol type)
    {
        string symbolName = type.ToDisplayString(FullyQualifiedTypeFormat);
        StringBuilder builder = new(symbolName.Length + 32);

        foreach (char character in symbolName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        builder.Append(".CollectTargetsInfo.g.cs");
        return builder.ToString();
    }

    private static string GenerateSource(TypeAnalysisResult result)
    {
        StringBuilder builder = new();

        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        if (!result.Type.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ");
            builder.AppendLine(result.Type.ContainingNamespace.ToDisplayString());
            builder.AppendLine("{");
        }

        ImmutableArray<INamedTypeSymbol> typeChain = EnumerateContainingTypes(result.Type).Reverse().ToImmutableArray();
        int indentationLevel = result.Type.ContainingNamespace.IsGlobalNamespace ? 0 : 1;

        foreach (INamedTypeSymbol type in typeChain)
        {
            AppendIndentation(builder,indentationLevel);
            builder.Append(GetTypeDeclarationHeader(type,type.Equals(result.Type,SymbolEqualityComparer.Default)));
            builder.AppendLine();

            foreach (string constraintClause in GetConstraintClauses(type))
            {
                AppendIndentation(builder,indentationLevel + 1);
                builder.AppendLine(constraintClause);
            }

            AppendIndentation(builder,indentationLevel);
            builder.AppendLine("{");
            indentationLevel++;
        }

        AppendGeneratedMembers(builder,indentationLevel,result.Entries);

        for (int index = typeChain.Length - 1; index >= 0; index--)
        {
            indentationLevel--;
            AppendIndentation(builder,indentationLevel);
            builder.AppendLine("}");
        }

        if (!result.Type.ContainingNamespace.IsGlobalNamespace)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static void AppendGeneratedMembers(StringBuilder builder,int indentationLevel,ImmutableArray<CollectedTargetEntry> entries)
    {
        AppendIndentation(builder,indentationLevel);
        builder.AppendLine("private static readonly global::System.Collections.Immutable.ImmutableDictionary<string, string> __doingCollectedTargetsNameToDescription");
        AppendIndentation(builder,indentationLevel + 1);
        builder.AppendLine("= CreateCollectedTargetsNameToDescription();");
        builder.AppendLine();

        AppendIndentation(builder,indentationLevel);
        builder.AppendLine("public static global::System.Collections.Immutable.ImmutableDictionary<string, string> TargetsNameToDescription");
        AppendIndentation(builder,indentationLevel + 1);
        builder.AppendLine("=> __doingCollectedTargetsNameToDescription;");
        builder.AppendLine();

        AppendIndentation(builder,indentationLevel);
        builder.AppendLine("private static global::System.Collections.Immutable.ImmutableDictionary<string, string> CreateCollectedTargetsNameToDescription()");
        AppendIndentation(builder,indentationLevel);
        builder.AppendLine("{");

        if (entries.Length == 0)
        {
            AppendIndentation(builder,indentationLevel + 1);
            builder.AppendLine("return global::System.Collections.Immutable.ImmutableDictionary.Create<string, string>(global::System.StringComparer.Ordinal);");
        }
        else
        {
            AppendIndentation(builder,indentationLevel + 1);
            builder.AppendLine("return global::System.Collections.Immutable.ImmutableDictionary.CreateRange<string, string>(");
            AppendIndentation(builder,indentationLevel + 2);
            builder.AppendLine("global::System.StringComparer.Ordinal,");
            AppendIndentation(builder,indentationLevel + 2);
            builder.AppendLine("new global::System.Collections.Generic.KeyValuePair<string, string>[]");
            AppendIndentation(builder,indentationLevel + 2);
            builder.AppendLine("{");

            foreach (CollectedTargetEntry entry in entries)
            {
                AppendIndentation(builder,indentationLevel + 3);
                builder.Append("new global::System.Collections.Generic.KeyValuePair<string, string>(");
                builder.Append(ToLiteral(entry.Name));
                builder.Append(", ");
                builder.Append(ToLiteral(entry.Description));
                builder.AppendLine("),");
            }

            AppendIndentation(builder,indentationLevel + 2);
            builder.AppendLine("});");
        }

        AppendIndentation(builder,indentationLevel);
        builder.AppendLine("}");
    }

    private static string GetTypeDeclarationHeader(INamedTypeSymbol type,bool addInterface)
    {
        StringBuilder builder = new();

        string accessibility = GetAccessibilityText(type.DeclaredAccessibility);
        if (accessibility.Length > 0)
        {
            builder.Append(accessibility);
            builder.Append(' ');
        }

        if (type.IsStatic)
        {
            builder.Append("static ");
        }
        else
        {
            if (type.IsAbstract)
            {
                builder.Append("abstract ");
            }

            if (type.IsSealed)
            {
                builder.Append("sealed ");
            }
        }

        builder.Append("partial ");
        builder.Append(type.IsRecord ? "record " : "class ");
        builder.Append(type.Name);

        if (type.TypeParameters.Length > 0)
        {
            builder.Append('<');
            builder.Append(string.Join(", ",type.TypeParameters.Select(static parameter => parameter.Name)));
            builder.Append('>');
        }

        if (addInterface)
        {
            builder.Append(" : global::Doing.Cli.Generator.ICollectedTargetsInfo");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> GetConstraintClauses(INamedTypeSymbol type)
    {
        foreach (ITypeParameterSymbol typeParameter in type.TypeParameters)
        {
            List<string> constraints = [];

            if (typeParameter.HasReferenceTypeConstraint)
            {
                constraints.Add(
                    typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                        ? "class?"
                        : "class");
            }
            else if (typeParameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }
            else if (typeParameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (typeParameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            constraints.AddRange(typeParameter.ConstraintTypes.Select(static constraintType => constraintType.ToDisplayString(FullyQualifiedTypeFormat)));

            if (typeParameter.HasConstructorConstraint && !typeParameter.HasValueTypeConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count == 0)
            {
                continue;
            }

            yield return $"where {typeParameter.Name} : {string.Join(", ",constraints)}";
        }
    }

    private static string GetAccessibilityText(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => string.Empty,
        };
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateContainingTypes(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            yield return current;
        }
    }

    private static void AppendIndentation(StringBuilder builder,int indentationLevel)
    {
        builder.Append(' ',indentationLevel * 4);
    }

    private static string ToLiteral(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');

        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\0':
                    builder.Append(@"\0");
                    break;
                case '\a':
                    builder.Append(@"\a");
                    break;
                case '\b':
                    builder.Append(@"\b");
                    break;
                case '\f':
                    builder.Append(@"\f");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                case '\v':
                    builder.Append(@"\v");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        builder.Append(@"\u");
                        builder.Append(((int)character).ToString("X4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private sealed class TypeAnalysisResult
    {
        public TypeAnalysisResult(
            INamedTypeSymbol type,
            ImmutableArray<CollectedTargetEntry> entries,
            ImmutableArray<Diagnostic> diagnostics,
            bool canGenerateSource)
        {
            Type = type;
            Entries = entries;
            Diagnostics = diagnostics;
            CanGenerateSource = canGenerateSource;
        }

        public INamedTypeSymbol Type { get; }

        public ImmutableArray<CollectedTargetEntry> Entries { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public bool CanGenerateSource { get; }
    }

    private readonly struct CollectedTargetEntry
    {
        public readonly string Name;
        public readonly string Description;

        public CollectedTargetEntry(string name,string description)
        {
            Name = name;
            Description = description;
        }
    }

    private static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor TypeMustBePartial = new(
            id: "DOING006",
            title: "CollectTargetsInfo types must be partial",
            messageFormat: "Types marked with '[CollectTargetsInfo]' must be partial. '{0}' is not partial.",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Source generation adds an interface implementation through a generated partial declaration, so the annotated type and its containing types must be partial.");

        public static readonly DiagnosticDescriptor DuplicateTargetName = new(
            id: "DOING007",
            title: "Duplicate collected target name",
            messageFormat: "Target name '{0}' is duplicated in '{1}'. The first declaration will be used.",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "CollectTargetsInfo collects target metadata by name. Duplicate names within the same annotated type are ignored after the first declaration and reported as warnings.");

        public static readonly DiagnosticDescriptor UnrecognizedTarget = new(
            id: "DOING008",
            title: "Target could not be statically collected",
            messageFormat: "Target member '{0}' in '{1}' could not be statically collected and will be ignored: {2}",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "CollectTargetsInfo only collects targets whose Name, Description, and construction chain can be determined at compile time. Targets that cannot be analyzed are ignored and reported as warnings.");
    }
}
