// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Doing.IO;

/// <summary>
/// Describes how an external process should be started and how its streams should be handled.
/// </summary>
/// <param name="ExecutableFile">The executable or command name to start.</param>
/// <param name="WorkingDirectory">The working directory used when the process starts.</param>
/// <param name="Arguments">The arguments passed to the executable in order.</param>
/// <param name="EnvironmentVariables">Optional environment variables to add or override for the child process.</param>
/// <param name="InheritEnv"><see langword="true"/> to inherit the current process environment before applying <paramref name="EnvironmentVariables"/>; otherwise only the provided variables are used.</param>
/// <param name="RedirectStdout"><see langword="true"/> to capture standard output into the returned <see cref="ProcessResult"/>.</param>
/// <param name="RedirectStderr"><see langword="true"/> to capture standard error into the returned <see cref="ProcessResult"/>.</param>
/// <param name="RedirectStdinTo">Text written to standard input before waiting for process completion. Set to <see langword="null"/> to leave standard input inheriting current process.</param>
public record ProcessSpec(
    string ExecutableFile,
    string WorkingDirectory,
    ImmutableArray<string> Arguments,
    Dictionary<string, string>? EnvironmentVariables = null,
    bool InheritEnv = true,
    bool RedirectStdout = false,
    bool RedirectStderr = false,
    string? RedirectStdinTo = "")
{
    /// <summary>
    /// Starts the configured process, waits for it to exit, and returns its captured result.
    /// </summary>
    /// <returns>A <see cref="ProcessResult"/> containing the exit code and any redirected output.</returns>
    /// <exception cref="IOException">Thrown when the process could not be started.</exception>
    public async Task<ProcessResult> Startup()
    {
        var startInfo = new ProcessStartInfo(ExecutableFile, Arguments)
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = RedirectStdout,
            RedirectStandardError = RedirectStderr,
            RedirectStandardInput = RedirectStdinTo is not null,
        };

        if (EnvironmentVariables is { } envs)
        {
            if (!InheritEnv)
            {
                startInfo.Environment.Clear();
            }

            foreach (var env in envs)
            {
                startInfo.EnvironmentVariables[env.Key] = env.Value;
            }
        }

        var process = Process.Start(startInfo);

        if (process is null)
        {
            throw new IOException($"failed to startup the process {this}");
        }

        List<Task> tasks = [];
        Task<string>? stdout = null;
        Task<string>? stderr = null;

        if (RedirectStdout)
        {
            stdout = process.StandardOutput.ReadToEndAsync();
            tasks.Add(stdout);
        }

        if (RedirectStderr)
        {
            stderr = process.StandardError.ReadToEndAsync();
            tasks.Add(stderr);
        }

        if (RedirectStdinTo is {} s)
        {
            tasks.Add(process.StandardInput.WriteAsync(s));
        }

        tasks.Add(process.WaitForExitAsync());

        await Task.WhenAll(tasks);

        return new ProcessResult(process.ExitCode, stdout?.Result, stderr?.Result);
    }
}
