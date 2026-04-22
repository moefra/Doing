
using Autofac;
using Doing.Cli;
using Doing.Core;
using Doing.IO;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.CommandLine.Parsing;
using Doing.Cli.Generator;

namespace Doing.Sample;

class Program(ParsedBuildingOptions buildingOptions,
              DPath projectRootDirectory)
    : DoingBuild(buildingOptions,projectRootDirectory)
{
    public static void Main(string[] args) =>
        Doing<Program>(args);

    public Target Zoo => New().Name("a").Description("");

    [HostBuilderHook]
    public static void ConfigureHost(HostApplicationBuilder hostBuilder)
    {
    }

    [HostDIHook]
    public static void ConfigureServices(ContainerBuilder builder)
    {
    }

    [CmdHook]
    public static void ConfigureCommand(RootCommand rootCommand)
    {
    }

    [BuildingOptionsHook]
    public static ParsedBuildingOptions ConfigureOptions(ParsedBuildingOptions options, ParseResult parseResult)
    {
        return options;
    }

    [BuildingDIHook]
    public static void ConfigureScopedServices(ContainerBuilder builder)
    {
    }

}
