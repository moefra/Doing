// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Doing.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class TaskFactoryGenerator : IIncrementalGenerator
{
    private const string TaskAttributeMetadataName = "Doing.Abstractions.TaskAttribute";
    private const string ParameterAttributeMetadataName = "Doing.Abstractions.ParameterAttribute";
    private const string TaskFactoryAttributeMetadataName = "Doing.Abstractions.TaskFactoryAttribute";
    private const string TaskInterfaceMetadataName = "Doing.Abstractions.ITask";
    private const string TaskFactoryInterfaceMetadataName = "Doing.Abstractions.ITaskFactory";
    private const string TaskExecutionContextMetadataName = "Doing.Abstractions.ITaskExecutionContext";
    private const string MonikerMetadataName = "Doing.Abstractions.Moniker";
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";
    private static readonly DiagnosticDescriptor InvalidTaskTypeDescriptor = new(
        id: "DOINGGEN001",
        title: "Invalid task type",
        messageFormat: "Task type '{0}' must be a non-abstract, non-generic class that implements Doing.Abstractions.ITask.",
        category: "Doing.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingTaskNameDescriptor = new(
        id: "DOINGGEN002",
        title: "Missing task name",
        messageFormat: "Task type '{0}' must declare an accessible static Name property of type Doing.Abstractions.Moniker.",
        category: "Doing.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingConstructorDescriptor = new(
        id: "DOINGGEN003",
        title: "Missing task constructor",
        messageFormat: "Task type '{0}' must declare an accessible constructor with parameters (Doing.Abstractions.ITaskExecutionContext, System.Threading.CancellationToken).",
        category: "Doing.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterNotWritableDescriptor = new(
        id: "DOINGGEN004",
        title: "Task parameter is not writable",
        messageFormat: "Task parameter property '{0}' on task type '{1}' must have an accessible set or init accessor.",
        category: "Doing.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var taskDeclarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: TaskAttributeMetadataName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (generatorContext, _) => AnalyzeTask(generatorContext))
            .Where(static result => result is not null);

        context.RegisterSourceOutput(taskDeclarations, static (productionContext, result) =>
        {
            foreach (var diagnostic in result!.Value.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic);
            }

            if (result.Value.Source is { } source)
            {
                productionContext.AddSource(source.HintName, source.Text);
            }
        });
    }

    private static TaskAnalysisResult? AnalyzeTask(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol taskType)
        {
            return null;
        }

        Compilation compilation = context.SemanticModel.Compilation;
        INamedTypeSymbol? taskInterface = compilation.GetTypeByMetadataName(TaskInterfaceMetadataName);
        INamedTypeSymbol? parameterAttribute = compilation.GetTypeByMetadataName(ParameterAttributeMetadataName);
        INamedTypeSymbol? monikerType = compilation.GetTypeByMetadataName(MonikerMetadataName);
        INamedTypeSymbol? taskExecutionContext = compilation.GetTypeByMetadataName(TaskExecutionContextMetadataName);
        INamedTypeSymbol? taskFactoryAttribute = compilation.GetTypeByMetadataName(TaskFactoryAttributeMetadataName);
        INamedTypeSymbol? taskFactoryInterface = compilation.GetTypeByMetadataName(TaskFactoryInterfaceMetadataName);
        INamedTypeSymbol? cancellationTokenType = compilation.GetTypeByMetadataName(CancellationTokenMetadataName);

        if (taskInterface is null
            || parameterAttribute is null
            || monikerType is null
            || taskExecutionContext is null
            || taskFactoryAttribute is null
            || taskFactoryInterface is null
            || cancellationTokenType is null)
        {
            return null;
        }

        List<Diagnostic> diagnostics = [];
        Location location = taskType.Locations.FirstOrDefault() ?? Location.None;

        if (taskType.TypeKind != TypeKind.Class || taskType.IsAbstract || taskType.IsGenericType || !Implements(taskType, taskInterface))
        {
            diagnostics.Add(Diagnostic.Create(
                InvalidTaskTypeDescriptor,
                location,
                taskType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            return new TaskAnalysisResult(null, [.. diagnostics]);
        }

        if (!HasAccessibleTaskName(taskType, monikerType, compilation))
        {
            diagnostics.Add(Diagnostic.Create(
                MissingTaskNameDescriptor,
                location,
                taskType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        if (!HasAccessibleConstructor(taskType, taskExecutionContext, cancellationTokenType, compilation))
        {
            diagnostics.Add(Diagnostic.Create(
                MissingConstructorDescriptor,
                location,
                taskType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }

        ImmutableArray<TaskParameterSpec> parameters = AnalyzeParameters(taskType, parameterAttribute, compilation, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new TaskAnalysisResult(null, [.. diagnostics]);
        }

        string taskTypeName = taskType.ToDisplayString(FullyQualifiedTypeFormat);
        string taskNamespace = taskType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : taskType.ContainingNamespace.ToDisplayString();
        string taskDisplayName = taskType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        string factoryName = $"{EscapeIdentifier(taskType.Name)}Factory";
        string taskFactoryAttributeName = taskFactoryAttribute.ToDisplayString(FullyQualifiedTypeFormat);
        string taskFactoryInterfaceName = taskFactoryInterface.ToDisplayString(FullyQualifiedTypeFormat);
        string hintName = $"{SanitizeHintName(taskType)}.Factory.g.cs";

        string source = RenderFactorySource(
            taskNamespace,
            factoryName,
            taskTypeName,
            taskDisplayName,
            taskFactoryAttributeName,
            taskFactoryInterfaceName,
            parameters);

        return new TaskAnalysisResult(
            new GeneratedSource(hintName, source),
            [.. diagnostics]);
    }

    private static ImmutableArray<TaskParameterSpec> AnalyzeParameters(
        INamedTypeSymbol taskType,
        INamedTypeSymbol parameterAttribute,
        Compilation compilation,
        List<Diagnostic> diagnostics)
    {
        Dictionary<string, TaskParameterSpec> parameters = new(StringComparer.Ordinal);

        for (INamedTypeSymbol? current = taskType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || parameters.ContainsKey(member.Name))
                {
                    continue;
                }

                if (!TryGetParameterAttribute(member, parameterAttribute, out bool required))
                {
                    continue;
                }

                Location location = member.Locations.FirstOrDefault() ?? taskType.Locations.FirstOrDefault() ?? Location.None;
                if (!IsWritable(member, compilation, taskType.ContainingAssembly))
                {
                    diagnostics.Add(Diagnostic.Create(
                        ParameterNotWritableDescriptor,
                        location,
                        member.Name,
                        taskType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                    continue;
                }

                bool isInitOnly = member.SetMethod?.IsInitOnly == true;
                bool isImmutableArray = IsImmutableArray(member.Type, out string? elementTypeName);
                parameters.Add(
                    member.Name,
                    new TaskParameterSpec(
                        member.Name,
                        required,
                        isInitOnly,
                        member.Type.ToDisplayString(FullyQualifiedTypeFormat),
                        isImmutableArray,
                        elementTypeName));
            }
        }

        return [.. parameters.Values.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)];
    }

    private static bool TryGetParameterAttribute(
        IPropertySymbol property,
        INamedTypeSymbol parameterAttribute,
        out bool required)
    {
        for (IPropertySymbol? current = property; current is not null; current = current.OverriddenProperty)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, parameterAttribute))
                {
                    continue;
                }

                required = attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is bool isRequired
                    && isRequired;
                return true;
            }
        }

        required = false;
        return false;
    }

    private static bool IsWritable(IPropertySymbol property, Compilation compilation, IAssemblySymbol within)
    {
        if (!compilation.IsSymbolAccessibleWithin(property, within))
        {
            return false;
        }

        return property.SetMethod is { } setMethod && compilation.IsSymbolAccessibleWithin(setMethod, within);
    }

    private static bool HasAccessibleTaskName(INamedTypeSymbol taskType, INamedTypeSymbol monikerType, Compilation compilation)
    {
        return taskType.GetMembers("Name")
            .OfType<IPropertySymbol>()
            .Any(member =>
                member.IsStatic
                && SymbolEqualityComparer.Default.Equals(member.Type, monikerType)
                && member.GetMethod is not null
                && compilation.IsSymbolAccessibleWithin(member, taskType.ContainingAssembly)
                && compilation.IsSymbolAccessibleWithin(member.GetMethod, taskType.ContainingAssembly));
    }

    private static bool HasAccessibleConstructor(
        INamedTypeSymbol taskType,
        INamedTypeSymbol taskExecutionContext,
        INamedTypeSymbol cancellationTokenType,
        Compilation compilation)
    {
        return taskType.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 2
            && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, taskExecutionContext)
            && SymbolEqualityComparer.Default.Equals(constructor.Parameters[1].Type, cancellationTokenType)
            && compilation.IsSymbolAccessibleWithin(constructor, taskType.ContainingAssembly));
    }

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol target)
    {
        return type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, target));
    }

    private static bool IsImmutableArray(ITypeSymbol type, out string? elementTypeName)
    {
        if (type is INamedTypeSymbol namedType
            && namedType.Arity == 1
            && namedType.ConstructedFrom.MetadataName == "ImmutableArray`1"
            && namedType.ContainingNamespace.ToDisplayString() == "System.Collections.Immutable")
        {
            elementTypeName = namedType.TypeArguments[0].ToDisplayString(FullyQualifiedTypeFormat);
            return true;
        }

        elementTypeName = null;
        return false;
    }

    private static string RenderFactorySource(
        string taskNamespace,
        string factoryName,
        string taskTypeName,
        string taskDisplayName,
        string taskFactoryAttributeName,
        string taskFactoryInterfaceName,
        ImmutableArray<TaskParameterSpec> parameters)
    {
        bool hasInitOnlyParameter = parameters.Any(static parameter => parameter.IsInitOnly);

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using Doing.Abstractions;");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(taskNamespace))
        {
            builder.Append("namespace ").Append(taskNamespace).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append('[').Append(taskFactoryAttributeName).AppendLine("]");
        builder.Append("public sealed class ").Append(factoryName).Append(" : ").Append(taskFactoryInterfaceName).AppendLine();
        builder.AppendLine("{");
        builder.Append("    public global::System.Collections.Immutable.ImmutableArray<global::Doing.Abstractions.Moniker> CreationAblity => global::System.Collections.Immutable.ImmutableArray.Create(")
            .Append(taskTypeName)
            .AppendLine(".Name);");
        builder.AppendLine();
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Doing.Abstractions.ITask> CreateAsync(");
        builder.AppendLine("        global::Doing.Abstractions.Moniker moniker,");
        builder.AppendLine("        global::Doing.Abstractions.PropertySet arguments,");
        builder.AppendLine("        global::Doing.Abstractions.ITaskExecutionContext context,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.Append("        if (moniker != ").Append(taskTypeName).AppendLine(".Name)");
        builder.AppendLine("        {");
        builder.Append("            throw new global::System.ArgumentException(\"Factory for task '")
            .Append(EscapeStringLiteral(taskDisplayName))
            .Append("' can not create moniker '\" + moniker + \"'.\", nameof(moniker));")
            .AppendLine();
        builder.AppendLine("        }");
        builder.AppendLine();

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            builder.Append("        bool hasParameter").Append(i).Append(" = arguments.Properties.TryGetValue(")
                .Append("global::Doing.Abstractions.Moniker<")
                .Append(taskTypeName)
                .Append(">.Create(\"")
                .Append(EscapeStringLiteral(parameter.Name))
                .AppendLine("\"), out var propertyValue" + i + ");");

            if (parameter.Required)
            {
                builder.Append("        if (!hasParameter").Append(i).AppendLine(")");
                builder.AppendLine("        {");
                builder.Append("            throw new global::System.ArgumentException(\"Missing required task parameter '")
                    .Append(EscapeStringLiteral(parameter.Name))
                    .Append("' for task '")
                    .Append(EscapeStringLiteral(taskDisplayName))
                    .AppendLine("'.\", nameof(arguments));");
                builder.AppendLine("        }");
            }
        }

        if (parameters.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append("        var task = new ").Append(taskTypeName).AppendLine("(context, cancellationToken);");

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            string extractionExpression = parameter.IsImmutableArray
                ? $"propertyValue{i}!.ExtractArray<{parameter.ImmutableArrayElementTypeName}>()!"
                : $"propertyValue{i}!.Extract<{parameter.TypeName}>()";

            builder.Append("        if (hasParameter").Append(i).AppendLine(")");
            builder.AppendLine("        {");

            if (parameter.IsInitOnly)
            {
                builder.Append("            SetInitProperty(task, \"")
                    .Append(EscapeStringLiteral(parameter.Name))
                    .Append("\", ")
                    .Append(extractionExpression)
                    .AppendLine(");");
            }
            else
            {
                builder.Append("            task.")
                    .Append(EscapeIdentifier(parameter.Name))
                    .Append(" = ")
                    .Append(extractionExpression)
                    .AppendLine(";");
            }

            builder.AppendLine("        }");
        }

        if (parameters.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine("        return new global::System.Threading.Tasks.ValueTask<global::Doing.Abstractions.ITask>(task);");

        if (hasInitOnlyParameter)
        {
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static void SetInitProperty<TTask, TValue>(TTask task, string propertyName, TValue value)");
            builder.AppendLine("    {");
            builder.AppendLine("        var property = typeof(TTask).GetProperty(");
            builder.AppendLine("            propertyName,");
            builder.AppendLine("            global::System.Reflection.BindingFlags.Instance");
            builder.AppendLine("            | global::System.Reflection.BindingFlags.Public");
            builder.AppendLine("            | global::System.Reflection.BindingFlags.NonPublic)");
            builder.AppendLine("            ?? throw new global::System.InvalidOperationException(\"Failed to find init-only property '\" + propertyName + \"'.\");");
            builder.AppendLine("        var setter = property.SetMethod");
            builder.AppendLine("            ?? throw new global::System.InvalidOperationException(\"Failed to find init-only setter for property '\" + propertyName + \"'.\");");
            builder.AppendLine("        setter.Invoke(task, new object?[] { value });");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static string SanitizeHintName(INamedTypeSymbol taskType)
    {
        return taskType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '[')
            .Replace('>', ']');
    }

    private static string EscapeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? "@" + identifier
            : identifier;
    }

    private static readonly SymbolDisplayFormat FullyQualifiedTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly struct TaskAnalysisResult
    {
        public TaskAnalysisResult(GeneratedSource? source, ImmutableArray<Diagnostic> diagnostics)
        {
            Source = source;
            Diagnostics = diagnostics;
        }

        public GeneratedSource? Source { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }

    private readonly struct GeneratedSource
    {
        public GeneratedSource(string hintName, string text)
        {
            HintName = hintName;
            Text = text;
        }

        public string HintName { get; }

        public string Text { get; }
    }

    private sealed class TaskParameterSpec
    {
        public TaskParameterSpec(
            string name,
            bool required,
            bool isInitOnly,
            string typeName,
            bool isImmutableArray,
            string? immutableArrayElementTypeName)
        {
            Name = name;
            Required = required;
            IsInitOnly = isInitOnly;
            TypeName = typeName;
            IsImmutableArray = isImmutableArray;
            ImmutableArrayElementTypeName = immutableArrayElementTypeName;
        }

        public string Name { get; }

        public bool Required { get; }

        public bool IsInitOnly { get; }

        public string TypeName { get; }

        public bool IsImmutableArray { get; }

        public string? ImmutableArrayElementTypeName { get; }
    }
}
