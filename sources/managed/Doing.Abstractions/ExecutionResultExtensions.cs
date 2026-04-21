// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public static class ExecutionResultExtensions
{
    extension(ExecutionResult executionResult)
    {
        public T? TryExtract<T>()
        {
            return executionResult switch
            {
                ExecutedExecutionResult { Result: { } property }   => property.TryExtract<T>(),
                CachedExecutionResult { Cache: {} cachedProperty } => cachedProperty.TryExtract<T>(),
                _                                                  => default
            };
        }

        public T Extract<T>()
        {
            return executionResult switch
            {
                ExecutedExecutionResult { Result: { } property }   => property.Extract<T>(),
                CachedExecutionResult { Cache: {} cachedProperty } => cachedProperty.Extract<T>(),
                _                                                  => throw new InvalidOperationException($"failed to extract value with type {typeof(T).FullName} from the property {executionResult}")
            };
        }

        public static ExecutedExecutionResult Executed(string report, Property? result = null)
            => new(report, result);

        public static FailedExecutionResult Failed(string reason, Exception? result = null)
            => new(reason, result);

        public static CachedExecutionResult Cached(string report, Property? result = null)
            => new(report, result);

        public static SkippedExecutionResult Skipped(string reason)
            => new(reason);
    }
}
