using System.Collections.Immutable;
using ReflectionAssembly = System.Reflection.Assembly;
using System.Reflection;
using Doing.Abstractions;
using Doing.Core;
using Doing.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doing.Generator.Tests;

public sealed class TaskFactoryGeneratorTests
{
    [Test]
    public async Task GeneratesFactoryAndBindsParametersAsync()
    {
        var compilation = CreateCompilation(
            """
            using System.Collections.Immutable;
            using System.Threading;
            using System.Threading.Tasks;
            using Doing.Abstractions;

            namespace Sample;

            [Task]
            public sealed class SampleTask : ITask, ITaskName
            {
                private readonly ITaskExecutionContext _context;
                private readonly CancellationToken _cancellationToken;

                public static Moniker Name => Moniker<SampleTask>.Create(nameof(SampleTask));

                [Parameter(required: true)]
                public string Message { get; set; } = "default-message";

                [Parameter]
                public ImmutableArray<string> Tags { get; init; } = ImmutableArray.Create("default-tag");

                [Parameter]
                public bool Enabled { get; set; } = true;

                public bool ContextAssigned => _context is not null;
                public bool TokenCanBeCanceled => _cancellationToken.CanBeCanceled;

                public SampleTask(ITaskExecutionContext context, CancellationToken cancellationToken)
                {
                    _context = context;
                    _cancellationToken = cancellationToken;
                }

                public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult<ExecutionResult>(new SkippedExecutionResult(Message));
            }
            """);

        var runResult = RunGenerator(compilation, out var outputCompilation);
        var generatedSource = runResult.Results.Single().GeneratedSources.SingleOrDefault().SourceText?.ToString();
        if (generatedSource is null || !generatedSource.Contains("SampleTaskFactory", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected the generator to produce a SampleTaskFactory type.");
        }

        ReflectionAssembly assembly = LoadAssembly(outputCompilation);
        Type factoryType = assembly.GetType("Sample.SampleTaskFactory")
            ?? throw new InvalidOperationException("Expected generated factory type.");
        if (Activator.CreateInstance(factoryType) is not ITaskFactory factory)
        {
            throw new InvalidOperationException("Expected generated type to implement ITaskFactory.");
        }

        Moniker expectedName = GetTaskMoniker(assembly, "Sample.SampleTask");
        if (factory.CreationAblity.Length != 1 || factory.CreationAblity[0] != expectedName)
        {
            throw new InvalidOperationException("Expected CreationAblity to expose the task moniker.");
        }

        Type taskType = assembly.GetType("Sample.SampleTask")
            ?? throw new InvalidOperationException("Expected generated sample task type.");

        var arguments = new PropertySet(
            new Dictionary<Moniker, Property>
            {
                [MonikerFromTask(taskType, "Message")] = new StringProperty("hello"),
                [MonikerFromTask(taskType, "Tags")] = new ArrayProperty(
                    ImmutableArray.Create<Property>(
                        new StringProperty("alpha"),
                        new StringProperty("beta"))),
                [MonikerFromTask(taskType, "Enabled")] = new BooleanProperty(false),
            }.ToImmutableDictionary());

        ITask created = await factory.CreateAsync(
            expectedName,
            arguments,
            new TestTaskExecutionContext(),
            new CancellationToken(canceled: true));

        if (created.GetType() != taskType)
        {
            throw new InvalidOperationException("Expected the generated factory to return the concrete task type.");
        }

        AssertPropertyEquals(created, "Message", "hello");
        AssertPropertyEquals(created, "Enabled", false);
        AssertPropertyEquals(created, "ContextAssigned", true);
        AssertPropertyEquals(created, "TokenCanBeCanceled", true);

        if (taskType.GetProperty("Tags")?.GetValue(created) is not ImmutableArray<string> tags
            || tags.Length != 2
            || tags[0] != "alpha"
            || tags[1] != "beta")
        {
            throw new InvalidOperationException("Expected init-only ImmutableArray parameter to be bound from PropertySet.");
        }
    }

    [Test]
    public async Task MissingOptionalParametersKeepDefaultsAndRequiredParametersThrowAsync()
    {
        var compilation = CreateCompilation(
            """
            using System.Collections.Immutable;
            using System.Threading;
            using System.Threading.Tasks;
            using Doing.Abstractions;

            namespace Sample;

            [Task]
            public sealed class OptionalTask : ITask, ITaskName
            {
                public static Moniker Name => Moniker<OptionalTask>.Create(nameof(OptionalTask));

                [Parameter(required: true)]
                public string Message { get; set; } = "required-default";

                [Parameter]
                public ImmutableArray<string> Tags { get; init; } = ImmutableArray.Create("fallback");

                [Parameter]
                public bool Enabled { get; set; } = true;

                public OptionalTask(ITaskExecutionContext context, CancellationToken cancellationToken)
                {
                }

                public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult<ExecutionResult>(new SkippedExecutionResult(Message));
            }
            """);

        RunGenerator(compilation, out var outputCompilation);
        ReflectionAssembly assembly = LoadAssembly(outputCompilation);
        Type taskType = assembly.GetType("Sample.OptionalTask")
            ?? throw new InvalidOperationException("Expected OptionalTask type.");
        Type factoryType = assembly.GetType("Sample.OptionalTaskFactory")
            ?? throw new InvalidOperationException("Expected OptionalTaskFactory type.");

        var factory = (ITaskFactory)(Activator.CreateInstance(factoryType)
            ?? throw new InvalidOperationException("Expected factory instance."));
        Moniker taskName = GetTaskMoniker(assembly, "Sample.OptionalTask");

        ITask taskWithDefaults = await factory.CreateAsync(
            taskName,
            new PropertySet(
                new Dictionary<Moniker, Property>
                {
                    [MonikerFromTask(taskType, "Message")] = new StringProperty("configured"),
                }.ToImmutableDictionary()),
            new TestTaskExecutionContext());

        AssertPropertyEquals(taskWithDefaults, "Message", "configured");
        AssertPropertyEquals(taskWithDefaults, "Enabled", true);

        if (taskType.GetProperty("Tags")?.GetValue(taskWithDefaults) is not ImmutableArray<string> tags
            || tags.Length != 1
            || tags[0] != "fallback")
        {
            throw new InvalidOperationException("Expected optional init-only property to keep its default value.");
        }

        try
        {
            await factory.CreateAsync(
                taskName,
                new PropertySet(ImmutableDictionary<Moniker, Property>.Empty),
                new TestTaskExecutionContext());
            throw new InvalidOperationException("Expected missing required parameter to throw.");
        }
        catch (ArgumentException exception) when (exception.Message.Contains("Message", StringComparison.Ordinal))
        {
        }

        try
        {
            await factory.CreateAsync(
                new Moniker("Sample", "OtherTask"),
                new PropertySet(ImmutableDictionary<Moniker, Property>.Empty),
                new TestTaskExecutionContext());
            throw new InvalidOperationException("Expected a mismatched moniker to throw.");
        }
        catch (ArgumentException exception) when (exception.ParamName == "moniker")
        {
        }
    }

    [Test]
    public void ReportsDiagnosticWhenTaskConstructorIsMissing()
    {
        var compilation = CreateCompilation(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Doing.Abstractions;

            namespace Sample;

            [Task]
            public sealed class InvalidTask : ITask, ITaskName
            {
                public static Moniker Name => Moniker<InvalidTask>.Create(nameof(InvalidTask));

                public InvalidTask()
                {
                }

                public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult<ExecutionResult>(new SkippedExecutionResult("noop"));
            }
            """);

        var runResult = RunGenerator(compilation, out _, allowGeneratorDiagnostics: true);
        if (!runResult.Diagnostics.Any(static diagnostic => diagnostic.Id == "DOINGGEN003"))
        {
            throw new InvalidOperationException("Expected DOINGGEN003 when the task constructor is missing.");
        }
    }

    [Test]
    public void ReportsDiagnosticWhenParameterPropertyIsNotWritable()
    {
        var compilation = CreateCompilation(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Doing.Abstractions;

            namespace Sample;

            [Task]
            public sealed class InvalidTask : ITask, ITaskName
            {
                public static Moniker Name => Moniker<InvalidTask>.Create(nameof(InvalidTask));

                [Parameter]
                public string Message { get; } = string.Empty;

                public InvalidTask(ITaskExecutionContext context, CancellationToken cancellationToken)
                {
                }

                public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult<ExecutionResult>(new SkippedExecutionResult("noop"));
            }
            """);

        var runResult = RunGenerator(compilation, out _, allowGeneratorDiagnostics: true);
        if (!runResult.Diagnostics.Any(static diagnostic => diagnostic.Id == "DOINGGEN004"))
        {
            throw new InvalidOperationException("Expected DOINGGEN004 when a task parameter can not be written.");
        }
    }

    private static GeneratorDriverRunResult RunGenerator(
        Compilation compilation,
        out Compilation outputCompilation,
        bool allowGeneratorDiagnostics = false)
    {
        var compilationErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (compilationErrors.Length > 0)
        {
            throw new InvalidOperationException(
                "Expected source compilation to succeed before running the generator: "
                + string.Join(Environment.NewLine, compilationErrors.Select(static diagnostic => diagnostic.ToString())));
        }

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? new CSharpParseOptions();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TaskFactoryGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out var diagnostics);

        if (!allowGeneratorDiagnostics && diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(
                "Expected generator driver to succeed: "
                + string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        var generatorException = runResult.Results.Select(static result => result.Exception).FirstOrDefault(static exception => exception is not null);
        if (generatorException is not null)
        {
            throw new InvalidOperationException("Expected generator to complete without throwing.", generatorException);
        }

        return runResult;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: $"Doing.Generator.Tests.Dynamic.{Guid.NewGuid():N}",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Preview))
            ],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();
        string trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Expected trusted platform assemblies.");

        foreach (var assemblyPath in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            references.Add(MetadataReference.CreateFromFile(assemblyPath));
        }

        references.Add(MetadataReference.CreateFromFile(typeof(TaskAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(TaskFactoryGenerator).Assembly.Location));

        return [.. references];
    }

    private static ReflectionAssembly LoadAssembly(Compilation compilation)
    {
        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                "Expected generated compilation to emit successfully: "
                + string.Join(Environment.NewLine, emitResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        peStream.Position = 0;
        return ReflectionAssembly.Load(peStream.ToArray());
    }

    private static Moniker GetTaskMoniker(ReflectionAssembly assembly, string taskTypeName)
    {
        Type taskType = assembly.GetType(taskTypeName)
            ?? throw new InvalidOperationException($"Expected task type '{taskTypeName}'.");
        PropertyInfo nameProperty = taskType.GetProperty(nameof(ITaskName.Name), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Expected static Name property.");

        return (Moniker)(nameProperty.GetValue(null)
            ?? throw new InvalidOperationException("Expected task moniker."));
    }

    private static Moniker MonikerFromTask(Type taskType, string propertyName)
    {
        return new Moniker(taskType.FullName ?? throw new InvalidOperationException("Expected task type full name."), propertyName);
    }

    private static void AssertPropertyEquals(object instance, string propertyName, object? expected)
    {
        object? actual = instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        if (!Equals(actual, expected))
        {
            throw new InvalidOperationException($"Expected property '{propertyName}' to be '{expected}', got '{actual}'.");
        }
    }

    private sealed class TestTaskExecutionContext : ITaskExecutionContext
    {
        public ILoggerFactory Factory => NullLoggerFactory.Instance;

        public BuildingPropertySet BuildingPropertySet { get; } = CreateBuildingPropertySet();

        private static BuildingPropertySet CreateBuildingPropertySet()
        {
            return new BuildingPropertySet(
                new PropertySet(
                    new Dictionary<Moniker, Property>
                    {
                        [Moniker<BuildingPropertySet>.Create(nameof(BuildingPropertySet.ContinueOnError))] = new BooleanProperty(false),
                        [Moniker<BuildingPropertySet>.Create(nameof(BuildingPropertySet.ShouldTreatWarningAsError))] = new BooleanProperty(false),
                    }.ToImmutableDictionary()),
                new PropertySet(ImmutableDictionary<Moniker, Property>.Empty));
        }
    }
}
