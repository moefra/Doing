// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.IO;

/// <summary>
/// Represents the final outcome of a completed process execution.
/// </summary>
/// <param name="ExitCode">The exit code returned by the process.</param>
/// <param name="Stdout">The captured standard output when output redirection was enabled; otherwise <see langword="null"/>.</param>
/// <param name="Stderr">The captured standard error when error redirection was enabled; otherwise <see langword="null"/>.</param>
public record ProcessResult(int ExitCode,string? Stdout,string? Stderr);
