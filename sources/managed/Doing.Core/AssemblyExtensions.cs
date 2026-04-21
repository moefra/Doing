// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Reflection;

namespace Doing.Core;

public static class AssemblyExtensions
{
    extension(Assembly assembly)
    {
        public List<Type> GetExportedTypeContainingAttribute<T>()
            where T: Attribute
        {
            List<Type> result = [];
            result.AddRange((IEnumerable<Type>)(from type in assembly.ExportedTypes let attribute = type.GetCustomAttribute<T>() select attribute).OfType<T>());
            return result;
        }
    }
}
