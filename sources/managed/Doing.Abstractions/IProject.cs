// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public interface IProject
{
    string Description { get; }
    PropertySet Metadata { get; }
}
