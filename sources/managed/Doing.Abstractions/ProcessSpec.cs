// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using Zio;

namespace Doing.Abstractions;

public record ProcessSpec(UPath ExecutableFile,
                          UPath WorkingDirectory,
                          Dictionary<string,string>? EnvironmentVariables,
                          bool RedirectStdout,
                          bool RedirectStderr,
                          bool RedirectStdin);
