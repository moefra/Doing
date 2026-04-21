// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Abstractions;

public static class PropertySetExtensions
{
    extension(PropertySet propertySet)
    {
        public PropertySet Join(PropertySet another)
        {
            Dictionary<Moniker, Property> properties = new(propertySet.Properties);

            foreach (var property in another.Properties)
            {
                properties[property.Key] = property.Value;
            }

            return new PropertySet(properties.ToImmutableDictionary());
        }
    }
}
