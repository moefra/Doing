// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using Zio;

namespace Doing.Abstractions;

public record ProcessSpec(UPath ExecutableFile,
                          UPath WorkingDirectory,
                          ImmutableArray<string> Arguments,
                          Dictionary<string,string>? EnvironmentVariables = null,
                          bool RedirectStdout = false,
                          bool RedirectStderr = false,
                          string? RedirectStdinTo = "");
