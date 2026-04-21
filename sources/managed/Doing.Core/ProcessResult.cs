// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Core;

public record ProcessResult(int ExitCode,string? Stdout,string? Stderr);
