// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Doing.Cli;
using Doing.Core;
using Microsoft.Extensions.Hosting;
using Serilog;

//namespace Doing.Cli;

public class DoingBuild : CoreDoingBuild
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
            builder.RegisterType<T>()
                   .SingleInstance()
                   .AsSelf()
                   .As<CoreDoingBuild>()
                   .As<DoingBuild>();

            var attributeType = typeof(DIHookAttribute);
            foreach (var method in thisType.GetMethods())
            {
                if (method.GetCustomAttribute(attributeType) is not null)
                {
                    method.Invoke(null, [builder]);
                }
            }
        });

        var host = hostBuilder.Build();

        var task = Run(host,args);

        return task.GetAwaiter().GetResult();
    }

    private static async Task<int> Run(IHost host,string[] args)
    {
        CancellationTokenSource source = new();

        Console.CancelKeyPress += (_,_) =>
        {
            source.Cancel();
        };

        await host.StartAsync(source.Token);

        try
        {
            var build = (DoingBuild?)host.Services.GetService(typeof(DoingBuild));

            if (build is null)
            {
                throw new InvalidOperationException("failed to get DoingBuild from the ServiceProvider of Host");
            }

            var rootCommand = new RootCommand("`doing` scripting system");

            var buildTargets = new Argument<string[]>("build targets")
            {
                Arity = ArgumentArity.OneOrMore,
                DefaultValueFactory = (_) =>
                {
                    return build.DefaultBuild.Select((target => target.Name)).ToArray();
                },
            };

            rootCommand.Arguments.Add(buildTargets);

            var buildingOptions = new BuildingOptions(rootCommand);

            build.CmdHook(rootCommand);

            var result = rootCommand.Parse(args);

            foreach (ParseError resultError in
                     result.Errors)
            {
                Log.Error("Program argument parse error:{error}", resultError);
            }

            var parsedBuildingOptions = buildingOptions.Parse(result);

            build.Options = build.OptionsHook(parsedBuildingOptions, result);

            string[]? parsedBuildTargets = result.GetValue(buildTargets);

            if (parsedBuildTargets is null)
            {
                throw new ArgumentException($"failed to parse {parsedBuildTargets}");
            }
            

        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // ignore
        }
        finally
        {
            await host.StopAsync(source.Token);
            await source.CancelAsync();
        }

        return 0;
    }
}
