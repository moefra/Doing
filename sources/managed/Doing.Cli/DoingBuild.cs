// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Features.AttributeFilters;
using Doing.Cli.Generator;
using Doing.Core;
using Doing.Core.Extensions;
using Doing.IO;
using Kawayi.Demystifier;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using ILogger = Serilog.ILogger;

namespace Doing.Cli;

public class DoingBuild(ParsedBuildingOptions options,DPath rootDirectory)
    : CoreDoingBuild(options,rootDirectory)
{
    public const string RootDirectoryPathKey = $"{nameof(RootDirectoryPathKey)}";

    private static bool IsOutputHelping(string[] args)
    {
        return args.Any(s => s is "-h" or "--help" or "-help" or "/?" or "-?" or "/help");
    }

    private static bool IsOutputVersion(string[] args)
    {
        return args.Any(s => s is "-v" or "-V" or "--version" or "-version" or "/v" or "/version");
    }

    protected static int Doing<T>(string[] args)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        if (IsOutputVersion(args))
        {
            Console.WriteLine($"doing script system {typeof(DoingBuild).Assembly.GetName().Version}");
            return 0;
        }
        var thisType = typeof(T);

        var logger = BuildLogger(IsOutputVersion(args) || IsOutputHelping(args));
        logger.Verbose("Startup with {type}", thisType);

        var (host,lifetime) = BuildHost<T>(logger);

        var task = Run<T>(host,lifetime,args,logger);

        return task.GetAwaiter().GetResult();
    }

    private static ILogger BuildLogger(bool silent)
    {
        var configuration = new LoggerConfiguration();

        configuration = silent ? configuration.MinimumLevel.Fatal() : configuration.MinimumLevel.Verbose();

        Log.Logger = configuration.WriteTo
                                  .Console(
            theme: AnsiConsoleTheme.Literate,
            outputTemplate:
            "-{Timestamp:HH:mm:ss} {Level}{NewLine}\t{Message:lj}{NewLine}{Exception}")
                                  .CreateLogger();

        var logger = Log.Logger.ForContext<DoingBuild>();
        return logger;
    }

    private static (IHost,ILifetimeScope) BuildHost<T>(ILogger logger)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);
        var hostBuilder = Host.CreateApplicationBuilder();

        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSerilog();

        logger.Verbose("Invoke [HostBuilderHook]");
        thisType.InvokeMethodWithAttribute<HostBuilderHookAttribute>(null, hostBuilder);

        hostBuilder.ConfigureContainer(new AutofacServiceProviderFactory(), (builder) =>
        {
            logger.Verbose("Invoke [HostDIHook]");
            thisType.InvokeMethodWithAttribute<HostDIHookAttribute>(null, builder);
        });

        var host = hostBuilder.Build();

        var lifetime = (ILifetimeScope)(host.Services.GetService(typeof(ILifetimeScope))
                                        ?? throw new InvalidOperationException("failed to resolve ILifetimeScope"));

        logger.Verbose("Built General host");

        return (host,lifetime);
    }

    private static Dictionary<string, string> GetKebabToNormalizedDictionary<T>()
        where T:ICollectedTargetsInfo
    {
        Dictionary<string,string> kebab = [];
        foreach (string key in T.TargetsNameToDescription.Keys)
        {
            kebab.Add(key.ToKebabCase(), key);
        }

        return kebab;
    }

    private static string GetTargetHelpText<T>()
        where T:ICollectedTargetsInfo
    {
        var kebab = GetKebabToNormalizedDictionary<T>();
        return $"possible values:\n\t{string.Join("\n\t",kebab.Select(pair => $"{pair.Key}({pair.Value}):{T.TargetsNameToDescription[pair.Value]}"))}";
    }

    private static async Task<int> RunHostAsync(IHost host,ILogger logger, Func<CancellationToken,Task> task)
    {
        bool failed = false;
        CancellationTokenSource source = new();

        try
        {
            await host.StartAsync(source.Token);

            await task(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception exception)
        {
            logger.Error(exception.Demystify(), "catch exception when building");
            failed = true;
        }
        finally
        {
            await host.StopAsync(source.Token);
            await source.CancelAsync();
        }

        return failed ? 1 : 0;
    }

    private static RootCommand GetRootCommand()
    {
        var rootCommand = new RootCommand("`doing` scripting system");
        return rootCommand;
    }

    private static async Task<(
        string[]? targets,
        ParsedBuildingOptions options,
        string projectRootDir,
        ParseResult result)> ConstructBuildingOptionsAsync<T>(
        string[] args,
        ILogger logger,
        CancellationToken cancellationToken)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);
        var rootCommand = GetRootCommand();

        var buildTargets = new Argument<string[]>("build targets") { Arity = ArgumentArity.ZeroOrMore, };
        buildTargets.Description = GetTargetHelpText<T>();
        rootCommand.Arguments.Add(buildTargets);

        var projectRootDir = new Option<string>("root-dir", "root");

        var dir = Environment.GetEnvironmentVariable("DOING_ROOT");
        if (dir is not null)
        {
            projectRootDir.Required = false;
            projectRootDir.DefaultValueFactory = _ => dir;
            logger.Verbose("Using DOING_ROOT as {root}", dir);
        }
        else
        {
            logger.Verbose("No environment variable 'DOING_ROOT' found");
            projectRootDir.Required = true;
        }

        rootCommand.Options.Add(projectRootDir);

        var buildingOptions = new BuildingOptions(rootCommand);

        logger.Verbose("Invoke [CmdHook]");
        thisType.InvokeMethodWithAttribute<CmdHookAttribute>(null, rootCommand);

        var result = rootCommand.Parse(args);

        foreach (ParseError resultError in
                 result.Errors)
        {
            Log.Error("Program argument parse error:{error}", resultError);
        }

        if (await result.InvokeAsync(cancellationToken: cancellationToken) != 0)
        {
            throw new InvalidOperationException("the CommandLineParseResult.Invoke() return non-zero values");
        }

        var parsedProjectRootDir = result.GetValue(projectRootDir)
                                   ?? throw new ArgumentException("failed to resolve RootDirectory argument");

        var parsedBuildingOptions = buildingOptions.Parse(result);

        logger.Verbose("Invoke [BuildingOptionsHook]");

        var attributeType = typeof(BuildingOptionsHookAttribute);
        foreach (var method in thisType.GetMethods())
        {
            if (method.GetCustomAttribute(attributeType) is not null)
            {
                parsedBuildingOptions = (ParsedBuildingOptions)(
                    method.Invoke(null, [parsedBuildingOptions, result])
                    ?? throw new InvalidOperationException("the BuildingOptionsHook must not return null")
                );
            }
        }

        return (result.GetValue(buildTargets), parsedBuildingOptions,parsedProjectRootDir, result);
    }

    private static void TriggerPropertyAccess(Type thisType,string[] targets,object @this)
    {
        var targetType = typeof(Target);
        foreach (var target in targets)
        {
            foreach (var method in thisType.GetProperty(target)?.GetAccessors()
                                   ?? throw new ArgumentException($"Invalid target name {target}"))
            {
                if (method.ReturnType.IsEquivalentTo(targetType))
                {
                    _ = method.Invoke(@this, null);
                }
            }
        }
    }

    private static async Task RunBuildingAsync<T>(string[] args,
                                                  ILifetimeScope lifetimeScope,
                                                  ILogger logger,
                                                  CancellationToken cancellationToken)
        where T : DoingBuild, ICollectedTargetsInfo
    {
        var thisType = typeof(T);
        var (targets, parsedBuildingOptions,parsedProjectRootDir, result) = await ConstructBuildingOptionsAsync<T>(
            args,
            logger, cancellationToken);

        if (IsOutputHelping(args))
        {
            return;
        }

        await using var subLifetime = lifetimeScope.BeginLifetimeScope((builder) =>
        {
            builder.RegisterInstance(parsedBuildingOptions)
                   .SingleInstance();

            builder.RegisterInstance(result)
                   .SingleInstance();

            builder.RegisterType<T>()
                   .SingleInstance()
                   .AsSelf()
                   .As<CoreDoingBuild>()
                   .As<DoingBuild>()
                   .WithAttributeFiltering();

            builder.RegisterInstance<DPath>(parsedProjectRootDir).Keyed<DPath>(RootDirectoryPathKey);

            logger.Verbose("Invoke [BuildingDIHook]");
            thisType.InvokeMethodWithAttribute<BuildingDIHookAttribute>(null, builder);
        });

        logger.Verbose("Resolve {type}", thisType);
        var build = subLifetime.Resolve<T>();

        var parsedBuildTargets =
            (targets is null || targets.Length == 0)
                ? build.DefaultBuild.Select((target => target.Name)).ToArray()
                : targets;

        var kebab = GetKebabToNormalizedDictionary<T>();
        parsedBuildTargets = parsedBuildTargets.Select((s =>
                                                           kebab.GetValueOrDefault(s, s)))
                                               .ToArray();

        TriggerPropertyAccess(thisType, parsedBuildTargets, build);

        logger.Verbose("Request to execute {targets}", parsedBuildTargets);

        await build.TaskSet.ExecuteAllAsync(parsedBuildTargets, cancellationToken);
    }

    private static async Task<int> Run<T>(IHost host,ILifetimeScope lifetimeScope,string[] args, ILogger logger)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        return await RunHostAsync(host, logger, async token =>
        {
            await RunBuildingAsync<T>(args, lifetimeScope, logger,token);
        });
    }
}
