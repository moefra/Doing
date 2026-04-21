// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Doing.IO;

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
