// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Core;

public sealed record BuildingPropertySet:PropertySet
{
    public BuildingPropertySet(
        PropertySet globalProperties,
        ImmutableDictionary<Moniker,Property> propertySet)
        : base(propertySet)
    {
        GlobalProperties = globalProperties.Properties;

        ContinueOnError = GlobalProperties[new Moniker(nameof(Doing),
                                                       nameof(ContinueOnError))].Extract<bool>();
        ShouldTreatWarningAsError = GlobalProperties[new Moniker(nameof(Doing),
                                                                 nameof(ShouldTreatWarningAsError))].Extract<bool>();
    }

    public bool ContinueOnError { get; }

    public bool ShouldTreatWarningAsError { get; }

    public IReadOnlyDictionary<Moniker, Property> GlobalProperties { get; }
}
