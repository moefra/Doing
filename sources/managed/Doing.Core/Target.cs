// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Diagnostics.CodeAnalysis;

namespace Doing.Core;

public class UnsourcedTarget
{
    public UnnamedTarget Source(ITaskContainer taskContainer)
    {
        return new UnnamedTarget() { Source = taskContainer.TaskSet };
    }
}

public class UnnamedTarget
{
    public required TaskSet Source { get; init; }

    public UndescriptedTarget Name(string name)
    {
        return new UndescriptedTarget() { Name = name, Source = Source };
    }
}

public class UndescriptedTarget
{
    public required TaskSet Source { get; init; }
    public required string Name { get; init; }

    public Target Description(string description)
    {
        var target = new Target() { Source = Source, Name = Name, Description = description };
        var task = Source.Targets[Name];
        if (task is Target existedTarget)
        {
            if (existedTarget.Description == description)
            {
                return existedTarget;
            }
        }
        Source.Targets[Name] = target;
        return target;
    }
}

public class Target : ITask
{
    public required TaskSet Source { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public string CommandLineName => Name.ToKebabCase();

    public List<Target> Dependencies { get; } = [];

    public Func<CancellationToken, Task> Action { get; set; } = _ => Task.CompletedTask;

    public Target()
    {

    }

    [SetsRequiredMembers]
    public Target(TaskSet set, string name, string description)
    {
        Source = set;
        Name = name;
        Description = description;
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
        Dependencies.AddRange(target);

        return this;
    }

    public Task Execute(CancellationToken cancellationToken = default) => Action?.Invoke(cancellationToken) ?? Task.CompletedTask;

    public static implicit operator Target(Func<Target> value)
    {
        return value();
    }
}
