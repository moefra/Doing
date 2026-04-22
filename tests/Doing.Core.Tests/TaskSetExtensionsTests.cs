// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Core.Tests;

public class TaskSetExtensionsTests
{
    [Test]
    public async Task Join_MergesTargetsFromBothTaskSets()
    {
        TaskSet left = CreateTaskSet();
        TaskSet right = CreateTaskSet();
        int leftCount = 0;
        int rightCount = 0;

        _ = new Target(left,"Left","Left target")
            .Executes(() => Interlocked.Increment(ref leftCount));

        _ = new Target(right,"Right","Right target")
            .Executes(() => Interlocked.Increment(ref rightCount));

        TaskSet joined = left.Join(right);

        await joined.ExecuteAllAsync(["Left","Right"]);

        await Assert.That(leftCount).IsEqualTo(1);
        await Assert.That(rightCount).IsEqualTo(1);
        await Assert.That(joined.Targets.Keys.Order(StringComparer.Ordinal).ToArray())
                    .IsEquivalentTo(["Left","Right"]);
    }

    [Test]
    public async Task Join_ThrowsForDuplicateTargetNames()
    {
        TaskSet left = CreateTaskSet();
        TaskSet right = CreateTaskSet();

        _ = new Target(left,"Build","Build target");
        _ = new Target(right,"Build","Another build target");

        Exception? exception = await CaptureExceptionAsync(() => Task.FromResult(left.Join(right)));

        await Assert.That(exception is InvalidOperationException).IsTrue();
        await Assert.That(exception?.Message.Contains("Build",StringComparison.Ordinal) ?? false).IsTrue();
    }

    [Test]
    public async Task ExecuteAllAsync_ExecutesSingleTargetOnce()
    {
        TaskSet set = CreateTaskSet();
        int runCount = 0;

        _ = new Target(set,"Build","Build target")
            .Executes(() => Interlocked.Increment(ref runCount));

        await set.ExecuteAllAsync(["Build"]);

        await Assert.That(runCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAllAsync_ExecutesDependenciesBeforeDependents()
    {
        TaskSet set = CreateTaskSet();
        List<string> executionOrder = [];

        Target dependency = new Target(set,"Prepare","Prepare target")
                            .Executes(() => executionOrder.Add("prepare"));

        _ = new Target(set,"Build","Build target")
            .DependsOn(dependency)
            .Executes(() => executionOrder.Add("build"));

        await set.ExecuteAllAsync(["Build"]);

        await Assert.That(string.Join(",",executionOrder)).IsEqualTo("prepare,build");
    }

    [Test]
    public async Task ExecuteAllAsync_DeduplicatesSharedDependencies()
    {
        TaskSet set = CreateTaskSet();
        int sharedCount = 0;
        int leftCount = 0;
        int rightCount = 0;

        Target shared = new Target(set,"Shared","Shared target")
                        .Executes(() => Interlocked.Increment(ref sharedCount));

        _ = new Target(set,"Left","Left target")
            .DependsOn(shared)
            .Executes(() => Interlocked.Increment(ref leftCount));

        _ = new Target(set,"Right","Right target")
            .DependsOn(shared)
            .Executes(() => Interlocked.Increment(ref rightCount));

        await set.ExecuteAllAsync(["Left","Right"]);

        await Assert.That(sharedCount).IsEqualTo(1);
        await Assert.That(leftCount).IsEqualTo(1);
        await Assert.That(rightCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAllAsync_RunsIndependentTargetsInParallel()
    {
        TaskSet set = CreateTaskSet();
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0;
        int started = 0;
        int observedParallel = 0;

        Func<CancellationToken, Task> action = async (cancellationToken) =>
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                Volatile.Write(ref observedParallel,1);
            }

            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult(true);
            }

            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref running);
        };

        _ = new Target(set,"Alpha","Alpha target").Executes(action);
        _ = new Target(set,"Beta","Beta target").Executes(action);

        Task execution = set.ExecuteAllAsync(["Alpha","Beta"]);

        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult(true);

        await execution;

        await Assert.That(Volatile.Read(ref observedParallel) == 1).IsTrue();
    }

    [Test]
    public async Task ExecuteAllAsync_ResolvesTargetByCommandLineName()
    {
        TaskSet set = CreateTaskSet();
        int runCount = 0;

        _ = new Target(set,"BuildAll","Build everything")
            .Executes(() => Interlocked.Increment(ref runCount));

        await set.ExecuteAllAsync(["build-all"]);

        await Assert.That(runCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAllAsync_ResolvesPlainTasksByDictionaryKey()
    {
        TaskSet set = CreateTaskSet();
        int runCount = 0;

        set.Targets.Add("plain-task",new InlineTask(() =>
        {
            Interlocked.Increment(ref runCount);
            return Task.CompletedTask;
        }));

        await set.ExecuteAllAsync(["plain-task"]);

        await Assert.That(runCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteAllAsync_ThrowsForUnknownTargets()
    {
        TaskSet set = CreateTaskSet();
        _ = new Target(set,"KnownTarget","Known target");

        Exception? exception = await CaptureExceptionAsync(() => set.ExecuteAllAsync(["missing-target"]));

        await Assert.That(exception is ArgumentException).IsTrue();
        await Assert.That(exception?.Message.Contains("KnownTarget",StringComparison.Ordinal) ?? false).IsTrue();
    }

    [Test]
    public async Task ExecuteAllAsync_ThrowsForCircularDependencies()
    {
        TaskSet set = CreateTaskSet();

        Target alpha = new Target(set,"Alpha","Alpha target");
        Target beta = new Target(set,"Beta","Beta target");

        alpha.DependsOn(beta);
        beta.DependsOn(alpha);

        Exception? exception = await CaptureExceptionAsync(() => set.ExecuteAllAsync(["Alpha"]));

        await Assert.That(exception is InvalidOperationException).IsTrue();
        await Assert.That(exception?.Message.Contains("Alpha -> Beta -> Alpha",StringComparison.Ordinal) ?? false).IsTrue();
    }

    [Test]
    public async Task ExecuteAllAsync_StopsDependentsWhenDependenciesFail()
    {
        TaskSet set = CreateTaskSet();
        bool downstreamExecuted = false;

        Target failing = new Target(set,"Failing","Failing target")
                         .Executes(() => throw new IOException("boom"));

        _ = new Target(set,"Downstream","Downstream target")
            .DependsOn(failing)
            .Executes(() => downstreamExecuted = true);

        Exception? exception = await CaptureExceptionAsync(() => set.ExecuteAllAsync(["Downstream"]));

        await Assert.That(exception is IOException).IsTrue();
        await Assert.That(downstreamExecuted).IsFalse();
    }

    [Test]
    public async Task ExecuteAllAsync_AllowsTargetsWithoutActions()
    {
        TaskSet set = CreateTaskSet();
        bool rootExecuted = false;

        Target dependency = new Target(set,"Dependency","Dependency target");

        _ = new Target(set,"Root","Root target")
            .DependsOn(dependency)
            .Executes(() => rootExecuted = true);

        await set.ExecuteAllAsync(["Root"]);

        await Assert.That(rootExecuted).IsTrue();
    }

    private static TaskSet CreateTaskSet() => new(new Dictionary<string,ITask>(StringComparer.Ordinal));

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class InlineTask(Func<Task> action) : ITask
    {
        public Task Execute(CancellationToken cancellationToken = default) => action();
    }
}
