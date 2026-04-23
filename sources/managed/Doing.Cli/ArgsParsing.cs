// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Collections.Immutable;
using System.Text;
using Doing.Core;

namespace Doing.Cli;

public sealed class ArgsParsing
{
    private bool _parsed = false;

    public ImmutableArray<string> ArgumentsToParse { get; }

    private void AssertParsed()
    {
        if (_parsed)
        {
            throw new InvalidOperationException($"must not operate on built {nameof(ArgsParsing)}");
        }
    }

    private void AssertDefinition(Definition definition)
    {
        if (InvalidDefinitionNames.TryGetValue(definition.Name, out var invalidName))
        {
            throw new ArgumentException($"the name of `{definition}` is invalid:name {invalidName} is reserved");
        }

        if (!definition.Name.Any(c => (!char.IsControl(c)) && (c == '_' || char.IsWhiteSpace(c) || char.IsLetterOrDigit(c))))
        {
            throw new ArgumentException($"the name of `{definition}` is invalid:contains invalid character");
        }

        if (definition.Name.StartsWith("-"))
        {
            throw new ArgumentException($"the name of `{definition}` is invalid:must not starts with character '-'");
        }
    }

    public static string ProgramName { get; } = nameof(Doing).ToLowerInvariant();

    public static string ProgramDescription { get; } = "scripting system";

    public static string ProgramVerion { get; } =
        typeof(ArgsParsing).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private Dictionary<string, PropertyDefinition> _propertyDefinitions { get; } = [];

    public IReadOnlyDictionary<string, PropertyDefinition> PropertyDefinitions
    {
        get
        {
            AssertParsed();
            return _propertyDefinitions;
        }
    }

    private Dictionary<string, TargetDefinition> _targetDefinitions { get; } = [];

    public IReadOnlyDictionary<string, TargetDefinition> TargetDefinitions
    {
        get
        {
            AssertParsed();
            return _targetDefinitions;
        }
    }

    public void AddTarget(TargetDefinition definition)
    {
        AssertParsed();
        AssertDefinition(definition);
        _targetDefinitions.Add(definition.Name, definition);
    }

    public void AddProperty(PropertyDefinition definition)
    {
        AssertParsed();
        AssertDefinition(definition);
        _propertyDefinitions.Add(definition.Name, definition);
    }

    private static ImmutableHashSet<string> IgnoreCaseStringSet(params IEnumerable<string> items)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var item in items)
        {
            builder.Add(item);
        }

        return builder.ToImmutable();
    }

    public static ImmutableHashSet<string> VersionFlagMarkers
    {
        get
        {
            field ??= IgnoreCaseStringSet("-v","-version","--version","/version","/v","version");
            return field;
        }
    }

    public static ImmutableHashSet<string> HelpFlagMarkers
    {
        get
        {
            field ??= IgnoreCaseStringSet("-h","-help","--help","/help","/h", "/?","help");
            return field;
        }
    }

    public static ImmutableHashSet<string> InvalidDefinitionNames
    {
        get
        {
            field ??= IgnoreCaseStringSet(
                VersionFlagMarkers.Concat(HelpFlagMarkers)
                                  .TakeWhile(s =>
                                                 !(s.StartsWith('/') || s.StartsWith('-'))));
            return field;
        }
    }

    private ArgsParsing(ImmutableArray<string> args)
    {
        ArgumentsToParse = args;
    }

    public static ShortcutTestResult TestArguments(string[] args)
    {
        var imArgs = args.ToImmutableArray();

        if(imArgs.FirstOrDefault(s => VersionFlagMarkers.TryGetValue(s, out _)) is {} trigger)
        {
            return new VersionFlagsDetected(static () =>
            {
                Console.WriteLine($"{ProgramName} {ProgramDescription} {ProgramVerion}");
                return 0;
            },trigger);
        }

        return new ParsingConstructed(new(imArgs));
    }

    private string ConstructHelpText()
    {
        var builder = new StringBuilder();

        builder.AppendLine($"{ProgramName} {ProgramDescription} {ProgramVerion}");
        builder.AppendLine($"usage: {Environment.GetCommandLineArgs()[0]} [options...] [building targets ro tun...]");
        builder.AppendLine($"use options to set properties");
        builder.AppendLine($"available usages:");
        builder.AppendLine($"\t--option=value\t--option value");
        builder.AppendLine();
        builder.AppendLine($"options:");
        builder.AppendLine();
        PrintHelpOfDefinition(PropertyDefinitions.Values ,"--");
        builder.AppendLine($"targets:");
        builder.AppendLine();
        PrintHelpOfDefinition(PropertyDefinitions.Values ,string.Empty);
        builder.AppendLine();
        builder.AppendLine($"bug report for {ProgramName}, access https://github.com/moefra/doing");

        return builder.ToString();

        void PrintHelpOfDefinition(IEnumerable<Definition> defs, string prefix)
        {
            foreach (var def in defs)
            {
                builder.AppendLine($"\t{prefix}{def.Name}:{def.Description}");
                if (def.Alias.Length != 0)
                {
                    builder.AppendLine($"\tAlias: {prefix}{string.Join($", {prefix}",def.Alias)}");
                }
                builder.AppendLine($"\t{def.DetailedHelpText.Replace("\n","\n\t")}");
                builder.AppendLine();
            }
        }
    }

    public ShortcutTestResult TestArgumentsAndBuild()
    {
        try
        {
            AssertParsed();

            if(ArgumentsToParse.FirstOrDefault(s => HelpFlagMarkers.TryGetValue(s, out _)) is {} trigger)
            {
                return new HelpFlagsDetected(() =>
                {
                    Console.WriteLine(ConstructHelpText());
                    return 0;
                },trigger);
            }

            var targets = _targetDefinitions.ToImmutableDictionary();

            var properties = _propertyDefinitions.ToImmutableDictionary();

            var targetsMap = _targetDefinitions.SelectMany(pair =>
            {
                var pairs = new List<KeyValuePair<string, TargetDefinition>>(pair.Value.Alias.Length) { pair };

                pairs.AddRange(pair.Value.Alias
                                   .Select(alias =>
                                               new KeyValuePair<string, TargetDefinition>(alias, pair.Value)));

                return pairs;
            }).ToImmutableDictionary();

            var propertiesMap = _propertyDefinitions.SelectMany(pair =>
            {
                var pairs = new List<KeyValuePair<string, PropertyDefinition>>(pair.Value.Alias.Length) { pair };

                pairs.AddRange(pair.Value.Alias
                                   .Select(alias =>
                                               new KeyValuePair<string, PropertyDefinition>(alias, pair.Value)));

                return pairs;
            }).ToImmutableDictionary();

            var targetsToRun = ImmutableArray.CreateBuilder<TargetDefinition>();

            var collectedProperties = ImmutableDictionary.CreateBuilder<PropertyDefinition, object>();

            int index = 0;
            while (index != ArgumentsToParse.Length)
            {
                var arg = ArgumentsToParse[index];
                var nextArg = (index + 1 != ArgumentsToParse.Length) ? ArgumentsToParse[index += 1] : null;

                if (arg.StartsWith("--"))
                {
                    arg = arg[2..];
                    if (propertiesMap.TryGetValue(arg, out var def))
                    {
                        var equalIndex = arg.LastIndexOf("=", StringComparison.InvariantCultureIgnoreCase);
                        if (equalIndex != -1)
                        {
                            var key = arg[0..equalIndex];
                            var value = (arg.Length == (equalIndex+1)) ? string.Empty : arg[(equalIndex + 1)..];
                            collectedProperties.Add(def, value);
                        }
                        else
                        {
                            if (nextArg is null)
                            {
                                throw new Exception($"the option `{arg}` must follow an argument when no equal(`=`) provided");
                            }
                            collectedProperties.Add(def, nextArg);
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"unknown option: `{arg}`");
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    return new UnknownArgumentDetected(() =>
                    {
                        Console.WriteLine($"unknown argument: `{arg}`");
                        return 1;
                    }, arg);
                }
                else
                {
                    if (targetsMap.TryGetValue(arg, out var target))
                    {
                        targetsToRun.Add(target);
                    }
                    else
                    {
                        throw new ArgumentException($"unknown target: `{arg}`");
                    }
                }

                index += 1;
            }

            return new ParsingFinished(null!);
        }
        finally
        {
            AssertParsed();
            _parsed = true;
        }
    }
}
