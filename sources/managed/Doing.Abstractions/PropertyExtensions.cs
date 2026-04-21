// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;

namespace Doing.Abstractions;

public static class PropertyExtensions
{
    extension(ArrayProperty property)
    {
        public ImmutableArray<T>? TryExtract<T>()
        {
            var builder = new List<T>(property.Value.Length);

            foreach (var value in property.Value)
            {
                if (value.TryExtract<T>() is {} v)
                {
                    builder.Add(v);
                }
                else
                {
                    return null;
                }
            }
            return [..builder];
        }

        public ImmutableArray<T> Extract<T>()
        {
            var builder = new List<T>(property.Value.Length);

            foreach (var value in property.Value)
            {
                builder.Add(value.Extract<T>());
            }
            return [..builder];
        }
    }

    extension(Property property)
    {
        public T? TryExtract<T>()
        {
            return property switch
            {
                StringProperty { Value: T v }  => v,
                IntegerProperty { Value: T v } => v,
                FloatProperty { Value: T v }   => v,
                BooleanProperty { Value: T v}  => v,
                ArrayProperty{ Value: T v }    => v,
                OpaqueProperty { Value:T v }   => v,
                _                              => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        public ImmutableArray<T>? TryExtractArray<T>()
        {
            return property switch
            {
                ArrayProperty p    => p.TryExtract<T>(),
                _                              => null
            };
        }

        public T Extract<T>()
        {
            return property switch
            {
                StringProperty { Value: T v }  => v,
                IntegerProperty { Value: T v } => v,
                FloatProperty { Value: T v }   => v,
                BooleanProperty { Value: T v}  => v,
                ArrayProperty{ Value: T v }    => v,
                OpaqueProperty { Value:T v }   => v,
                _                              => throw new InvalidOperationException($"failed to extract value with type {typeof(T).FullName} from the property {property}")
            };
        }

        public ImmutableArray<T>? ExtractArray<T>()
        {
            return property switch
            {
                ArrayProperty p => p.Extract<T>(),
                _               => null
            };
        }
    }
}
