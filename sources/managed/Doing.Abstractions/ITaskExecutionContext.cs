// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using Doing.Core;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.HighPerformance;
using Zio;

namespace Doing.Abstractions;

public interface ITaskExecutionContext
{
    ILoggerFactory Factory { get; }
    BuildingPropertySet BuildingPropertySet { get; }
}

public interface ITaskContextDateTimeExtension : ITaskExecutionContext
{
    Instant BuildingTimestamp { get; }
    IClock Clock { get; }
}

public interface ITaskContextFileSystemExtension : ITaskExecutionContext
{
    IFileSystem FileSystem { get; }
}

public interface ITaskContextProcessExtension : ITaskContextFileSystemExtension
{
    IProcessRunner Runner { get; }
}
