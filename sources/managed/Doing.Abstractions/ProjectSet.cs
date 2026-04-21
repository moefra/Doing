// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Abstractions;

public sealed record ProjectSet(ImmutableArray<IProject> Projects)
{

}
