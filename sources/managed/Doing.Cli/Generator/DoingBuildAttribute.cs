// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Cli.Generator;

/// <summary>
/// this attribute is a mark and **must not** be analyzer or generator's trigger
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DoingBuildAttribute : Attribute
{

}
