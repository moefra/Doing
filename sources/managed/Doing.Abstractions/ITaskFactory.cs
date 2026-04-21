// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public interface ITaskFactory
{
    ValueTask<ITask> CreateAsync(Moniker moniker,PropertySet arguments);
}
