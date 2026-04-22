// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Core.Exceptions;

public class CycleDependencyException : Exception
{
    public CycleDependencyException() : base()
    {
    }

    public CycleDependencyException(string targetName) : base(targetName)
    {
        TargetDependencyStack.Push(targetName);
    }

    public CycleDependencyException(string? msg, Exception? inner) : base(msg, inner)
    {
    }

    public Stack<string> TargetDependencyStack { get; } = [];
}
