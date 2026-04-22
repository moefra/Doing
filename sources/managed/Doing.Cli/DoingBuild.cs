// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Doing.Cli.Generator;
using Doing.Core;
using Doing.IO;
using Microsoft.Extensions.Hosting;
using Serilog;

//namespace Doing.Cli;

namespace Doing.Cli;

public class DoingBuild(ParsedBuildingOptions options,DPath rootDirectory)
    : CoreDoingBuild(options,rootDirectory)
{
    protected static int Doing<T>(string[] args)
        where T : DoingBuild
    {
        Log.Logger = new LoggerConfiguration()
                     .WriteTo.Console()
                     .CreateLogger();

        var hostBuilder = Host.CreateApplicationBuilder();

        hostBuilder.Logging.AddSerilog();

        var thisType = typeof(T);
        var attributeType = typeof(HostBuilderHookAttribute);
        foreach (var method in thisType.GetMethods())
        {
            if (method.GetCustomAttribute(attributeType) is not null)
            {
                method.Invoke(null, [hostBuilder]);
            }
        }
        hostBuilder.ConfigureContainer(new AutofacServiceProviderFactory(), (builder) =>
        {
            var attributeType = typeof(HostDIHookAttribute);
            foreach (var method in thisType.GetMethods())
            {
                if (method.GetCustomAttribute(attributeType) is not null)
                {
                    method.Invoke(null, [builder]);
                }
            }
        });

        var host = hostBuilder.Build();

        var lifetime = (ILifetimeScope)(host.Services.GetService(typeof(ILifetimeScope))
                                        ?? throw new InvalidOperationException("failed to resolve ILifetimeScope"));

        var task = Run<T>(host,lifetime,args);

        return task.GetAwaiter().GetResult();
    }

    private static async Task<int> Run<T>(IHost host,ILifetimeScope lifetimeScope,string[] args)
    {
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

            }

            var buildingOptions = new BuildingOptions(rootCommand);

            var thisType = typeof(T);
            var attributeType = typeof(CmdHookAttribute);
            foreach (var method in thisType.GetMethods())
            {
                if (method.GetCustomAttribute(attributeType) is not null)
                {
                    method.Invoke(null, [rootCommand]);
                }
            }

            var result = rootCommand.Parse(args);

            foreach (ParseError resultError in
                     result.Errors)
            {
                Log.Error("Program argument parse error:{error}", resultError);
            }

            var parsedBuildingOptions = buildingOptions.Parse(result);

            attributeType = typeof(BuildingOptionsHookAttribute);
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

                attributeType = typeof(BuildingDIHookAttribute);
                foreach (var method in thisType.GetMethods())
                {
                    if (method.GetCustomAttribute(attributeType) is not null)
                    {
                        method.Invoke(null, [builder]);
                    }
                }
            });

            var build = subLifetime.Resolve<DoingBuild>();

            string[]? parsedBuildTargets = result.GetValue(buildTargets);

            if (parsedBuildTargets is null)
            {
                throw new ArgumentException($"failed to parse {parsedBuildTargets}");
            }

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
