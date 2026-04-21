// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Core;

public sealed record BuildingPropertySet:PropertySet
{
    public BuildingPropertySet(
        PropertySet globalProperties,
        PropertySet propertySet)
        : base(globalProperties.Join(propertySet))
    {
        GlobalProperties = globalProperties.Properties;

        ContinueOnError = GlobalProperties[Moniker<BuildingPropertySet>
                                               .Create(nameof(ContinueOnError))]
            .Extract<bool>();
        ShouldTreatWarningAsError = GlobalProperties[Moniker<BuildingPropertySet>
                                                         .Create(nameof(ShouldTreatWarningAsError))]
            .Extract<bool>();
    }

    public bool ContinueOnError { get; }

    public bool ShouldTreatWarningAsError { get; }

    public ImmutableDictionary<Moniker, Property> GlobalProperties { get; }
}
