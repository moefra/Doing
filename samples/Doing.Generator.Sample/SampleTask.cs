// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Generator.Sample;

[Task]
public sealed class SampleTask : ITask, ITaskName
{
    private readonly ITaskExecutionContext _context;
    private readonly CancellationToken _cancellationToken;

    public static Moniker Name => Moniker<SampleTask>.Create(nameof(SampleTask));

    [Parameter(required: true)]
    public string Message { get; set; } = string.Empty;

    [Parameter]
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;

    public SampleTask(ITaskExecutionContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _cancellationToken = cancellationToken;
    }

    public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _ = _context;
        _ = _cancellationToken;
        _ = cancellationToken;

        return Task.FromResult<ExecutionResult>(new SkippedExecutionResult(Message));
    }
}
