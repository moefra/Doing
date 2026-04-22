// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Reflection;

namespace Doing.Core.Extensions;

public static class TypeExtensions
{
    extension(Type type)
    {
        public ImmutableArray<object?> InvokeMethodWithAttribute<T>(object? @this = null, params object?[]? args)
            where T:Attribute
        {
            var arrayBuilder = ImmutableArray.CreateBuilder<object?>();
            foreach (var method in type.GetMethods())
            {
                if (method.GetCustomAttribute(typeof(T)) is not null)
                {
                    arrayBuilder.Add(method.Invoke(@this,args));
                }
            }
            return arrayBuilder.ToImmutable();
        }
    }
}
