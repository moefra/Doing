// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Core;

public interface IDependentTask : ITask
{
    ImmutableArray<string> Dependencies { get; }
}
