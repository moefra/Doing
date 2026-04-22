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

namespace Doing.Cli;

public class DoingBuild(ParsedBuildingOptions options,DPath rootDirectory)
    : CoreDoingBuild(options,rootDirectory)
{
    public const string RootDirectoryPathKey = $"{nameof(RootDirectoryPathKey)}";

    protected static int Doing<T>(string[] args)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);

        Log.Logger = new LoggerConfiguration()
                     .WriteTo.Console(
                         theme:AnsiConsoleTheme.Literate,
                         outputTemplate:"-{Timestamp:HH:mm:ss} {Level:u3}{NewLine}\t{Message:lj}{NewLine}{Exception}")
                     .MinimumLevel.Verbose()
                     .CreateLogger();

        var logger = Log.Logger.ForContext<DoingBuild>();

        logger.Verbose("Startup with {type}", thisType);

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

        var task = Run<T>(host,lifetime,args,logger);

        return task.GetAwaiter().GetResult();
    }

    private static async Task<int> Run<T>(IHost host,ILifetimeScope lifetimeScope,string[] args, Serilog.ILogger logger)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);
        bool failed = false;
        CancellationTokenSource source = new();

        await host.StartAsync(source.Token);

        try
        {
            var rootCommand = new RootCommand("`doing` scripting system");

            var buildTargets = new Argument<string[]>("build targets")
            {
                Arity = ArgumentArity.ZeroOrMore,
            };

            rootCommand.Arguments.Add(buildTargets);

            var projectRootDir = new Option<string>("root-dir", "root");

            var dir = Environment.GetEnvironmentVariable("DOING_ROOT");
            if (dir is not null)
            {
                projectRootDir.Required = false;
                projectRootDir.DefaultValueFactory = _ => dir;
                logger.Verbose("Using DOING_ROOT as {root}",dir);
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
                       .WithAttributeFiltering();;

                builder.RegisterInstance<DPath>(parsedProjectRootDir).Keyed<DPath>(RootDirectoryPathKey);

                logger.Verbose("Invoke [BuildingDIHook]");
                thisType.InvokeMethodWithAttribute<BuildingDIHookAttribute>(null, builder);
            });

            logger.Verbose("Resolve {type}",thisType);
            var build = subLifetime.Resolve<T>();

            var targets = result.GetValue(buildTargets);

            var parsedBuildTargets =
                (targets is null || targets.Length == 0)
                ? build.DefaultBuild.Select((target => target.Name)).ToArray()
                : targets;

            logger.Verbose("Request to execute {targets}", parsedBuildTargets);

            await build.TaskSet.ExecuteAllAsync(parsedBuildTargets, source.Token);
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
}
