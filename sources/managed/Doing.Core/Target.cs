// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Core;

public abstract class Target : ITarget
{
    public abstract Task<ExecutionResult> ExecuteAsync();

    public Moniker Name { get; }

    public ImmutableArray<Moniker> Requirements { get; }

    public Target(Moniker name,ImmutableArray<Moniker> requirements)
    {
        Name = name;
        Requirements = requirements;
    }
}
