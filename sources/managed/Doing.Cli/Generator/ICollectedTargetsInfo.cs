// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Cli.Generator;

public interface ICollectedTargetsInfo
{
    static abstract ImmutableDictionary<string, string> TargetsNameToDescription { get; }
}
