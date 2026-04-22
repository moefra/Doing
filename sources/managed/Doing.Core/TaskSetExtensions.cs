// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Doing.Core.Exceptions;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Doing.Core;

public static class TaskSetExtensions
{
    extension(TaskSet set)
    {
        public TaskSet Join(TaskSet other)
        {
            ArgumentNullException.ThrowIfNull(other);

            var mergedTargets =
                new Dictionary<string, IDependentTask>(set.Targets.Count + other.Targets.Count, set.Targets.Comparer);

            foreach ((string name, IDependentTask task) in set.Targets)
            {
                mergedTargets.Add(name, task);
            }

            foreach ((string name, IDependentTask task) in other.Targets)
            {
                if (!mergedTargets.TryAdd(name, task))
                {
                    throw new InvalidOperationException($"Task '{name}' already exists in the joined TaskSet.");
                }
            }

            return new TaskSet(mergedTargets);
        }

        public async Task ExecuteAllAsync(
            string[] targetNames,
            CancellationToken cancellationToken = default)
        {
            HashSet<string> entered = [];
            HashSet<string> executed = [];

            foreach (var targetName in targetNames)
            {
                await set.ExecuteOneAsync(targetName, entered, executed, cancellationToken);
            }
        }

        private async Task ExecuteOneAsync(
            string targetName,
            HashSet<string> entered,
            HashSet<string> executed,
            CancellationToken cancellationToken = default)
        {
            if (!entered.Add(targetName))
            {
                throw new CycleDependencyException(targetName);
            }

            if (executed.Contains(targetName))
            {
                return;
            }

            if (!set.Targets.TryGetValue(targetName, out var target))
            {
                entered.Remove(targetName);
                throw new KeyNotFoundException($"Task '{targetName}' not found.");
            }

            try
            {
                foreach (string dependency in target.Dependencies)
                {
                    try
                    {
                        await set.ExecuteOneAsync(dependency, entered, executed, cancellationToken);
                    }
                    catch (CycleDependencyException ex)
                    {
                        ex.TargetDependencyStack.Push(targetName);
                        throw;
                    }
                }

                await target.Execute(cancellationToken);
                Log.Verbose("Executed {target}",targetName);
                executed.Add(targetName);
            }
            finally
            {
                entered.Remove(targetName);
            }
        }
    }
}
