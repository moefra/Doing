// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions.Exceptions;

public sealed class TaskFailedException : Exception
{
    public TaskFailedException() { }
    public TaskFailedException(string? msg):base(msg){}
    public TaskFailedException(string? msg,Exception? inner):base(msg,inner){}
    public TaskFailedException(Moniker taskName,string failMsg,Exception? inner)
        :base($"Task({taskName}) failed:{failMsg}",inner){}
}
