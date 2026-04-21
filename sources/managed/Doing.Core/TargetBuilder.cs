// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Doing.Abstractions;

namespace Doing.Core;

public sealed class TargetBuilder : ITarget
{
    public TargetBuilder(string name)
    {
        var trace = new System.Diagnostics.StackTrace();
        var frame = trace.GetFrame(1);
        Name = new Moniker(frame?.GetMethod()?.DeclaringType?.FullName
                           ?? throw new InvalidOperationException("failed to construct target name from stack"), name);
    }

    public Task<ExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_read)
        {
            throw new InvalidOperationException("can not add dependency after access the properties");
        }
        _read = true;
        return _func.Invoke(cancellationToken);
    }

    public Moniker Name { get; }

    private bool _read = false;

    private Func<CancellationToken, Task<ExecutionResult>> _func = _ => Task.FromResult(ExecutionResult.Executed(string.Empty, null));

    private List<Moniker> _requirements = [];

    private ImmutableArray<Moniker>? _builtRequirements = null;

    public ImmutableArray<Moniker> Requirements
    {
        get
        {
            _read = true;

            _builtRequirements ??= [.._requirements];

            return _builtRequirements.Value;
        }
    }

    public TargetBuilder DependOn(params ITarget[] targets)
    {
        if (_read)
        {
            throw new InvalidOperationException("can not add dependency after access the properties");
        }

        _requirements.AddRange(
            from target in targets
            select target.Name);

        return this;
    }

    public TargetBuilder Execute(Action action)
    {
        if (_read)
        {
            throw new InvalidOperationException("can not add dependency after access the properties");
        }

        _func += (_) =>
        {
            try
            {
                action();
                return Task.FromResult(ExecutionResult.Executed(string.Empty, null));
            }
            catch (Exception exception)
            {
                return Task.FromException<ExecutionResult>(exception);
            }
        };

        return this;
    }

    public TargetBuilder Execute(Action<CancellationToken> action)
    {
        if (_read)
        {
            throw new InvalidOperationException("can not add dependency after access the properties");
        }

        _func += (token) =>
        {
            try
            {
                action(token);
                return Task.FromResult(ExecutionResult.Executed(string.Empty, null));
            }
            catch (Exception exception)
            {
                return Task.FromException<ExecutionResult>(exception);
            }
        };

        return this;
    }


    public TargetBuilder Execute(Func<CancellationToken, Task<ExecutionResult>> action)
    {
        if (_read)
        {
            throw new InvalidOperationException("can not add dependency after access the properties");
        }

        _func += action;

        return this;
    }
}
