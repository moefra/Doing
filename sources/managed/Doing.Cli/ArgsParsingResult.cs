// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Core;

namespace Doing.Cli;

public sealed class ArgsParsingResult
{
    /// <summary>
    /// the keys containing the name and aliases
    /// </summary>
    public ImmutableDictionary<string, TargetDefinition> TargetMap { get; }

    /// <summary>
    /// the keys containing the name and aliases
    /// </summary>
    public ImmutableDictionary<string, PropertyDefinition> PropertyMap { get; }


}
