// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Abstractions;

public interface ITaskFactory
{
    ImmutableArray<Moniker> CreationAblity { get; }
    ValueTask<ITask> CreateAsync(Moniker moniker,
                                 PropertySet arguments,
                                 ITaskExecutionContext context,
                                 CancellationToken cancellationToken = default);
}
