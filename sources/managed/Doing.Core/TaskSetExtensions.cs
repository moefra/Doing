// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Doing.Core;

public static class TaskSetExtensions
{
    extension(TaskSet set)
    {
        public TaskSet Join(TaskSet other)
        {
            ArgumentNullException.ThrowIfNull(other);

            var mergedTargets = new Dictionary<string,ITask>(set.Targets.Count + other.Targets.Count,set.Targets.Comparer);

            foreach ((string name,ITask task) in set.Targets)
            {
                mergedTargets.Add(name,task);
            }

            foreach ((string name,ITask task) in other.Targets)
            {
                if (!mergedTargets.TryAdd(name,task))
                {
                    throw new InvalidOperationException($"Task '{name}' already exists in the joined TaskSet.");
                }
            }

            return new TaskSet(mergedTargets);
        }

        public async Task ExecuteAllAsync(string[] target,CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(target);

            if (target.Length == 0)
            {
                return;
            }

            ITask[] requestedTasks = target.Select((targetName) => ResolveTarget(set,targetName)).ToArray();

            var collectedTasks = new HashSet<ITask>(ReferenceEqualityComparer.Instance);
            var visitingTargets = new HashSet<Target>(ReferenceEqualityComparer.Instance);
            var visitedTargets = new HashSet<Target>(ReferenceEqualityComparer.Instance);
            var currentPath = new List<Target>();

            foreach (ITask requestedTask in requestedTasks)
            {
                CollectDependencies(requestedTask,collectedTasks,visitingTargets,visitedTargets,currentPath);
            }

            using CancellationTokenSource linkedCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var failures = new ConcurrentQueue<ExceptionDispatchInfo>();
            var executionTasks = new ConcurrentDictionary<ITask,Lazy<Task>>(ReferenceEqualityComparer.Instance);

            Task[] rootExecutions = requestedTasks.Select((task) => ExecuteTaskAsync(task,linkedCancellationTokenSource,failures,executionTasks))
                                                 .ToArray();

            try
            {
                await Task.WhenAll(rootExecutions);
            }
            catch when (!failures.IsEmpty || cancellationToken.IsCancellationRequested)
            {
                // The original failures are surfaced below after all started work has settled.
            }

            if (!failures.IsEmpty)
            {
                ThrowFailures(failures);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static ITask ResolveTarget(TaskSet set,string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            throw new ArgumentException("Target name cannot be null or whitespace.",nameof(requestedName));
        }

        if (set.Targets.TryGetValue(requestedName,out ITask? exactMatch))
        {
            return exactMatch;
        }

        var commandLineMatches = new HashSet<Target>(ReferenceEqualityComparer.Instance);

        foreach (Target task in set.Targets.Values.OfType<Target>())
        {
            if (string.Equals(task.CommandLineName,requestedName,StringComparison.Ordinal))
            {
                commandLineMatches.Add(task);
            }
        }

        return commandLineMatches.Count switch
        {
            1 => commandLineMatches.First(),
            > 1 => throw new InvalidOperationException(
                $"Target '{requestedName}' is ambiguous. Matching targets: {string.Join(", ", commandLineMatches.Select((task) => task.Name).Order(StringComparer.Ordinal))}"),
            _ => throw new ArgumentException(
                $"Unknown target '{requestedName}'. Available targets: {FormatAvailableTargets(set)}",
                nameof(requestedName)),
        };
    }

    private static string FormatAvailableTargets(TaskSet set)
    {
        string[] availableTargets = set.Targets
                                       .Select((pair) => FormatTargetName(pair.Key,pair.Value))
                                       .Distinct(StringComparer.Ordinal)
                                       .Order(StringComparer.Ordinal)
                                       .ToArray();

        return availableTargets.Length == 0 ? "<none>" : string.Join(", ",availableTargets);
    }

    private static string FormatTargetName(string dictionaryKey,ITask task)
    {
        if (task is not Target target)
        {
            return dictionaryKey;
        }

        if (string.Equals(dictionaryKey,target.CommandLineName,StringComparison.Ordinal))
        {
            return dictionaryKey;
        }

        return $"{dictionaryKey} ({target.CommandLineName})";
    }

    private static void CollectDependencies(
        ITask task,
        HashSet<ITask> collectedTasks,
        HashSet<Target> visitingTargets,
        HashSet<Target> visitedTargets,
        List<Target> currentPath)
    {
        collectedTasks.Add(task);

        if (task is not Target target)
        {
            return;
        }

        if (visitedTargets.Contains(target))
        {
            return;
        }

        if (!visitingTargets.Add(target))
        {
            int cycleStart = currentPath.FindIndex((pathTarget) => ReferenceEquals(pathTarget,target));
            IEnumerable<string> cycle = currentPath.Skip(cycleStart)
                                                   .Append(target)
                                                   .Select((pathTarget) => pathTarget.Name);

            throw new InvalidOperationException($"Circular dependency detected: {string.Join(" -> ",cycle)}");
        }

        currentPath.Add(target);

        foreach (Target dependency in target.Dependencies)
        {
            CollectDependencies(dependency,collectedTasks,visitingTargets,visitedTargets,currentPath);
        }

        currentPath.RemoveAt(currentPath.Count - 1);
        visitingTargets.Remove(target);
        visitedTargets.Add(target);
    }

    private static Task ExecuteTaskAsync(
        ITask task,
        CancellationTokenSource cancellationTokenSource,
        ConcurrentQueue<ExceptionDispatchInfo> failures,
        ConcurrentDictionary<ITask,Lazy<Task>> executionTasks)
    {
        Lazy<Task> execution = executionTasks.GetOrAdd(
            task,
            (currentTask) => new Lazy<Task>(
                () => ExecuteTaskCoreAsync(currentTask,cancellationTokenSource,failures,executionTasks),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return execution.Value;
    }

    private static async Task ExecuteTaskCoreAsync(
        ITask task,
        CancellationTokenSource cancellationTokenSource,
        ConcurrentQueue<ExceptionDispatchInfo> failures,
        ConcurrentDictionary<ITask,Lazy<Task>> executionTasks)
    {
        if (task is Target target && target.Dependencies.Count > 0)
        {
            Task[] dependencyTasks = target.Dependencies.Select((dependency) => ExecuteTaskAsync(dependency,cancellationTokenSource,failures,executionTasks))
                                                        .ToArray();

            await Task.WhenAll(dependencyTasks);
        }

        cancellationTokenSource.Token.ThrowIfCancellationRequested();

        try
        {
            await task.Execute(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Enqueue(ExceptionDispatchInfo.Capture(exception));
            cancellationTokenSource.Cancel();
            throw;
        }
    }

    private static void ThrowFailures(ConcurrentQueue<ExceptionDispatchInfo> failures)
    {
        Exception[] exceptions = failures.Select((failure) => failure.SourceException).ToArray();

        if (exceptions.Length == 1)
        {
            failures.TryPeek(out ExceptionDispatchInfo? failure);
            failure?.Throw();
        }

        throw new AggregateException(exceptions);
    }
}
