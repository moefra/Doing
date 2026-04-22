
namespace Doing.Building;

[DoingBuild]
[CollectTargetsInfo]
partial class Program(ParsedBuildingOptions buildingOptions,
                      DPath projectRootDirectory) : DoingBuild(buildingOptions, projectRootDirectory)
{
    static void Main(string[] args) => Doing<Program>(args);

    public override Target[] DefaultBuild { get; }

    public Target BuildDotnet => New().Name("BuildDotnet")
                                      .Description("build the dotnet part of doing")
                                      .Executes(() =>
                                      {
                                          "dotnet".AsExecutable(projectRootDirectory,
                                                                "build");
                                      });
}
