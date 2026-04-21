// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public static class PropertyExtensions
{
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
    }
}
