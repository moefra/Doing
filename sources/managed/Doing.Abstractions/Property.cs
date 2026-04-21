// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Globalization;

namespace Doing.Abstractions;

public sealed record StringProperty(string Value) : Property;

public sealed record IntegerProperty(ulong Value) : Property;

public sealed record FloatProperty(double Value) : Property;

public sealed record BooleanProperty(bool Value) : Property;

public sealed record OpaqueProperty(object Value) : Property;

public sealed record ArrayProperty(ImmutableArray<Property> Value) : Property;

public abstract record Property();
