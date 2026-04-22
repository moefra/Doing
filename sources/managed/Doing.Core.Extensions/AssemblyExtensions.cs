// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Reflection;

namespace Doing.Core.Extensions;

public static class AssemblyExtensions
{
    extension(Assembly assembly)
    {
        public ImmutableArray<Type> GetExportedTypeContainingAttribute<T>(bool inherit)
            where T: Attribute
        {
            var result = ImmutableArray.CreateBuilder<Type>();
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.GetCustomAttribute(typeof(T), inherit) is not null)
                {
                    result.Add(type);
                }
            }
            return result.ToImmutable();
        }
    }
}
