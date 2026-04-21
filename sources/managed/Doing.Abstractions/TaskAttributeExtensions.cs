// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Doing.Abstractions;

public static class TaskAttributeExtensions
{
    extension(IServiceCollection collection)
    {
        public void AddTasks(IEnumerable<Type> tasks)
        {
            foreach (var task in tasks)
            {
                if (!task.IsAssignableTo(typeof(ITaskName)))
                {
                    throw new ArgumentException(
                        $"the type {task.FullName} registered to IServiceCollection can not assign to {nameof(ITaskName)}",
                        nameof(tasks));
                }

                var property = task.GetProperty(nameof(ITaskName.Name));
                if (property is null)
                {
                        throw new ArgumentException(
                            $"the type {task.FullName} registered to IServiceCollection can not get property {nameof(ITaskName.Name)}",
                            nameof(tasks));
                }

                var value = property.GetValue(null);

                if (value is Moniker name)
                {
                    throw new ArgumentException(
                        $"the field {nameof(ITaskName.Name)} of the type {task.FullName} registered to IServiceCollection can not convert to {nameof(Moniker)}",
                        nameof(tasks));
                }
            }
        }
    }
}
