
using Autofac.Features.AttributeFilters;
using Serilog;

namespace Doing.Building;

[DoingBuild]
[CollectTargetsInfo]
public partial class Program(ParsedBuildingOptions buildingOptions,
                      [KeyFilter(DoingBuild.RootDirectoryPathKey)] DPath projectRootDirectory)
    : DoingBuild(buildingOptions,
    projectRootDirectory)
{
    static void Main(string[] args) => Doing<Program>(args);

    public override Target[] DefaultBuild => [BuildManaged];

    public Target BuildDotnet => New().Name(nameof(BuildDotnet))
                                      .Description("build the dotnet part of doing")
                                      .Executes(async () =>
                                      {
                                          await "dotnet".AsExecutable(projectRootDirectory,
                                                                      "build")
                                                        .Startup();
                                      });

    public Target BuildManaged => New().Name(nameof(BuildManaged))
                                       .Description("build the managed code part of doing")
                                       .DependsOn(BuildDotnet);
}
