// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Text;
using Doing.IO;

namespace Doing.Core;

public static class StringExtensions
{
    extension(string input)
    {
        /// <summary>
        /// Convert camelCase or PascalCase to kebab-case.
        /// Examples:
        /// "helloWorld" -> "hello-world"
        /// "HelloWorld" -> "hello-world"
        /// "HTTPRequest" -> "http-request"
        /// "userID" -> "user-id"
        /// </summary>
        public string ToKebabCase()
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sb = new StringBuilder(input.Length + 8);

            for (int i = 0; i < input.Length; i++)
            {
                char current = input[i];

                if (char.IsUpper(current))
                {
                    bool hasPrevious = i > 0;
                    bool hasNext = i + 1 < input.Length;

                    // Insert '-' in these cases:
                    // 1. lower/digit -> Upper   e.g. helloWorld -> hello-world
                    // 2. acronym boundary       e.g. HTTPRequest -> http-request
                    if (hasPrevious)
                    {
                        char previous = input[i - 1];

                        bool previousIsLowerOrDigit = char.IsLower(previous) || char.IsDigit(previous);
                        bool acronymBoundary = char.IsUpper(previous) && hasNext && char.IsLower(input[i + 1]);

                        if (previousIsLowerOrDigit || acronymBoundary)
                        {
                            sb.Append('-');
                        }
                    }

                    sb.Append(char.ToLowerInvariant(current));
                }
                else if (current == '_' || current == '-' || current == ' ')
                {
                    // Normalize separators to '-'
                    if (sb.Length > 0 && sb[^1] != '-')
                        sb.Append('-');
                }
                else
                {
                    sb.Append(current);
                }
            }

            return sb.ToString().Trim('-');
        }

        /// <summary>
        /// Convert kebab-case to camelCase.
        /// Example:
        /// "hello-world" -> "helloWorld"
        /// </summary>
        public string KebabToCamelCase()
        {
            return input.FromSeparatedCase(upperFirst: false);
        }

        /// <summary>
        /// Convert kebab-case to PascalCase.
        /// Example:
        /// "hello-world" -> "HelloWorld"
        /// </summary>
        public string KebabToPascalCase()
        {
            return input.FromSeparatedCase(upperFirst: true);
        }

        private string FromSeparatedCase(bool upperFirst)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sb = new StringBuilder(input.Length);
            bool capitalizeNext = upperFirst;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '-' || c == '_' || c == ' ')
                {
                    capitalizeNext = true;
                    continue;
                }

                if (sb.Length == 0 && !upperFirst)
                {
                    sb.Append(char.ToLowerInvariant(c));
                    capitalizeNext = false;
                    continue;
                }

                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                capitalizeNext = false;
            }

            return sb.ToString();
        }

        public ProcessSpec AsExecutable(params string[] args)
        {
            return new ProcessSpec(
                input,
                Global.ProjectRoot,
                [..args]
            );
        }
    }
}
