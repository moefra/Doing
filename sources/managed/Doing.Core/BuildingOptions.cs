// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.CommandLine;

namespace Doing.Core;

public class BuildingOptions
{
    public Option<bool> ContinueOnError { get; }

    public Option<bool> TreatWarningAsError { get; }

    public BuildingOptions(RootCommand rootCommand)
    {
        ContinueOnError = new("continue-on-error", "keep-going");
        ContinueOnError.Required = false;
        ContinueOnError.DefaultValueFactory = (_) => false;
        TreatWarningAsError = new("treat-warnings-as-error", "fatal-warnings");
        TreatWarningAsError.Required = false;
        TreatWarningAsError.DefaultValueFactory = (_) => false;

        rootCommand.Options.Add(ContinueOnError);
        rootCommand.Options.Add(TreatWarningAsError);
    }

    public ParsedBuildingOptions Parse(ParseResult result)
    {
        return new ParsedBuildingOptions(
            result.GetValue(ContinueOnError),
            result.GetValue(TreatWarningAsError));
    }
}
