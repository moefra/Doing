// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Cli;

public abstract record ShortcutTestResult;

public sealed record ParsingConstructed(ArgsParsing ArgsParsing) : ShortcutTestResult;

public sealed record ParsingFinished(ArgsParsingResult ArgsParsingResult) : ShortcutTestResult;

public abstract record ShouldExit(Func<int> CallBeforeExiting) : ShortcutTestResult;

public sealed record VersionFlagsDetected(Func<int> CallBeforeExiting,string TriggerArgument) : ShouldExit(CallBeforeExiting);


public sealed record UnknownArgumentDetected(Func<int> CallBeforeExiting,string Unknown) : ShouldExit(CallBeforeExiting);

public sealed record HelpFlagsDetected(Func<int> CallBeforeExiting,string TriggerArgument) : ShouldExit(CallBeforeExiting);
