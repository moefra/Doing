// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Doing.Cli.Generator;
using Doing.Core;
using Doing.Core.Extensions;
using Doing.IO;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Doing.Cli;

public class DoingBuild(ParsedBuildingOptions options,DPath rootDirectory)
    : CoreDoingBuild(options,rootDirectory)
{
    protected static int Doing<T>(string[] args)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);

        Log.Logger = new LoggerConfiguration()
                     .WriteTo.Console()
                     .CreateLogger();

        var logger = Log.Logger.ForContext<DoingBuild>();

        logger.Verbose("Startup with {type}", thisType);

        var hostBuilder = Host.CreateApplicationBuilder();

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

    private static async Task<int> Run<T>(IHost host,ILifetimeScope lifetimeScope,string[] args, ILogger logger)
        where T : DoingBuild,ICollectedTargetsInfo
    {
        var thisType = typeof(T);
        bool failed = false;
        CancellationTokenSource source = new();

        Console.CancelKeyPress += (_,_) =>
        {
            source.Cancel();
        };

        await host.StartAsync(source.Token);

        try
        {
            var rootCommand = new RootCommand("`doing` scripting system");

            var buildTargets = new Argument<string[]>("build targets")
            {
                Arity = ArgumentArity.OneOrMore,
                DefaultValueFactory = (_) => [],
            };

            rootCommand.Arguments.Add(buildTargets);

            var projectRootDir = new Option<DirectoryInfo>("root-dir","root")
            {
                Arity = ArgumentArity.ExactlyOne,
            };

            var dir = Environment.GetEnvironmentVariable("DOING_ROOT");
            if (dir is not null)
            {
                projectRootDir.Required = false;
                projectRootDir.DefaultValueFactory = _ => new DirectoryInfo(dir);
                logger.Verbose("Using DOING_ROOT as {root}",dir);
            }
            else
            {
                logger.Verbose("No environment variable 'DOING_ROOT' found");
                projectRootDir.Required = true;
            }

            var buildingOptions = new BuildingOptions(rootCommand);

            logger.Verbose("Invoke [CmdHook]");
            thisType.InvokeMethodWithAttribute<CmdHookAttribute>(null, rootCommand);

            var result = rootCommand.Parse(args);

            foreach (ParseError resultError in
                     result.Errors)
            {
                Log.Error("Program argument parse error:{error}", resultError);
            }

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
                       .As<DoingBuild>();

                logger.Verbose("Invoke [BuildingDIHook]");
                thisType.InvokeMethodWithAttribute<BuildingDIHookAttribute>(null, builder);
            });

            logger.Verbose("Resolve {type}",thisType);
            var build = subLifetime.Resolve<T>();

            var parsedBuildTargets = result.GetValue(buildTargets) ??
                                           build.DefaultBuild.Select((target => target.Name)).ToArray();

            logger.Verbose("Execute {targets}", parsedBuildTargets);

            await build.TaskSet.ExecuteAllAsync(parsedBuildTargets, source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception)
        {
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
