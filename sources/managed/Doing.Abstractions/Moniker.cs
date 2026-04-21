// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

public readonly record struct Moniker()
{
    public readonly string Namespace;
    public readonly string Name;

    public static bool CheckNamespace(string @namespace)
    => @namespace.All((c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '.' || c == '`'))
       && @namespace.Split('.', StringSplitOptions.None).All((s) => !string.IsNullOrWhiteSpace(s));

    public static bool CheckName(string name)
        => name.All((c => char.IsAsciiLetterOrDigit(c) || c == '_'));

    public Moniker(string @namespace, string name) : this()
    {
        if (!CheckNamespace(@namespace))
        {
            throw new ArgumentException("the namespace is invalid",nameof(@namespace));
        }

        if (!CheckName(name))
        {
            throw new ArgumentException("the namespace is invalid",nameof(name));
        }

        Namespace = @namespace;
        Name = name;
    }

    public override string ToString() => $"{Namespace}.{Name}";
}

public static class Moniker<T>
{
    public static Moniker Create(string name)
    {
        return new Moniker(typeof(T).FullName!, name);
    }
}
