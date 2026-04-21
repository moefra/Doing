// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;
using Microsoft.Extensions.Hosting;

namespace Doing.Core;

public class CoreDoingBuild : ITaskContainer
{
    public virtual void CmdHook(RootCommand rootCommand)
    {
        return;
    }

    public virtual ParsedBuildingOptions OptionsHook(ParsedBuildingOptions options, ParseResult parseResult)
    {
        return options;
    }

    public virtual Target[] DefaultBuild { get; } = [];

    public virtual ParsedBuildingOptions Options { get; protected set; } = null!;

    public TaskSet TaskSet { get; } = new([]);
}
