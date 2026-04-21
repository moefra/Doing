// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Abstractions;

/// <summary>
/// Used by <see cref="TaskAttribute"/>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ParameterAttribute : Attribute
{
    public bool Required { get; }

    public ParameterAttribute(bool required = false)
    {
        Required = required;
    }
}
