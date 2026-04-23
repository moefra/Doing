// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Core;

public record PropertyDefinition(
    string Name,
    string Description,
    string DetailedHelpText,
    ImmutableArray<string> Alias,
    Type Type,
    ImmutableArray<string> PossibleValues) : Definition(Name, Description, DetailedHelpText, Alias);
