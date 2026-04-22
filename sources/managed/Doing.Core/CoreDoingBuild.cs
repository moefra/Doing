// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using Doing.IO;
using Microsoft.Extensions.Hosting;

namespace Doing.Core;

public class CoreDoingBuild : ITaskContainer
{
    public CoreDoingBuild(ParsedBuildingOptions options,DPath rootDirectory)
    {
        Options = options;
        RootDirectory = rootDirectory;
    }

    public virtual Target[] DefaultBuild { get; } = [];

    public ParsedBuildingOptions Options { get; }

    public DPath RootDirectory { get; }

    public TaskSet TaskSet { get; } = new([]);

    public UnnamedTarget New()
    {
        return new UnnamedTarget() { Source = TaskSet };
    }

    public void EnsureFilesystemCaseSensitive()
    {
        if (!FileSystem.IsPathCaseSensitive(RootDirectory))
        {
            throw new InvalidOperationException(
                $"the filesystem at root directory `{RootDirectory}` must be case-sensitive");
        }
    }
}
