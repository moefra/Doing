// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Core;

public sealed class ActionTarget : Target
{
    public ActionTarget(Moniker name,
                        ImmutableArray<Moniker> requirements,
                        Func<Task<ExecutionResult>> task)
        : base(name, requirements)
    {
        _task = task;
    }

    private readonly Func<Task<ExecutionResult>> _task;

    public override Task<ExecutionResult> ExecuteAsync() => _task.Invoke();
}
