// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public abstract record ExecutionResult();

public sealed record FailedExecutionResult(string Reason, Exception? Exception) : ExecutionResult;

public sealed record ExecutedExecutionResult(string Report, Property? Result) : ExecutionResult;

public sealed record CachedExecutionResult(string Report, Property? Cache) : ExecutionResult;

public sealed record SkippedExecutionResult(string Reason) : ExecutionResult;

