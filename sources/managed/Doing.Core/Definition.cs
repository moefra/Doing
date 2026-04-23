// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Core;

/// <summary>
///
/// </summary>
/// <param name="Name"></param>
/// <param name="Description"></param>
/// <param name="DetailedHelpText">can be multiline</param>
/// <param name="Alias"></param>
public record Definition(string Name,
                             string Description,
                             string DetailedHelpText,
                             ImmutableArray<string> Alias);
