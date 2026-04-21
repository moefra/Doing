// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Core;

public class Target : ITask
{
    public string Name { get; }

    public string Description { get; }

    public string CommandLineName { get; }

    public List<Target> Dependencies { get; } = [];

    public Func<CancellationToken, Task> Action { get; set; } = _ => Task.CompletedTask;

    public Target(TaskSet container,string name, string description)
    {
        Name = name;
        Description = description;
        CommandLineName = name.ToKebabCase();
        container.Targets.Add(Name,this);
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
}
