// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Doing.Core;

public sealed class UnnamedTarget
{
    public required TaskSet Source { get; init; }

    public UndescriptedTarget Name(string name)
    {
        return new UndescriptedTarget() { Name = name, Source = Source };
    }
}

public sealed class UndescriptedTarget
{
    public required TaskSet Source { get; init; }
    public required string Name { get; init; }

    public Target Description(string description)
    {
        var target = new Target(Source, Name, description);
        return target;
    }
}

public sealed class Target : IDependentTask
{
    public TaskSet Source { get; init; }
    public string Name { get; init; }
    public string Description { get; init; }

    public string CommandLineName => Name.ToKebabCase();

    public ImmutableArray<string> Dependencies => _dependencies.ToImmutableArray();

    private List<string> _dependencies = [];

    public Func<CancellationToken, Task> Action { get; set; } = _ => Task.CompletedTask;

    public Target(TaskSet set, string name, string description)
    {
        Source = set;
        Name = name;
        Description = description;
        set.Targets.Add(Name,this);
    }

    public Target Executes(Action action)
    {
        Action += (_) =>
        {
            action();
            return Task.CompletedTask;
        };
        return this;
    }

    public Target Executes(Func<Task> action)
    {
        Action += _ => action();
        return this;
    }

    public Target Executes(Func<CancellationToken, Task> action)
    {
        Action += action;
        return this;
    }

    public Target DependsOn(params Target[] target)
    {
        _dependencies.AddRange(target.Select(dep => dep.Name));
        return this;
    }

    public Task Execute(CancellationToken cancellationToken = default)
        => Action.Invoke(cancellationToken);
}
