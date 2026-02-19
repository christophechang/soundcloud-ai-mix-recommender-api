using System;
using System.Collections.Generic;
using System.Linq;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TagExtractor
    {
        public static IReadOnlyList<string> Extract(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return Array.Empty<string>();
            }

            string[] lines = description
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None);

            string? tagLine = FindTagLine(lines);
            if (string.IsNullOrWhiteSpace(tagLine))
            {
                return Array.Empty<string>();
            }

            string inner = ExtractBracketInner(tagLine);
            if (string.IsNullOrWhiteSpace(inner))
            {
                return Array.Empty<string>();
            }

            string cleaned = NormalizeHeader(inner);

            var tokens = cleaned
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return tokens;
        }

        private static string? FindTagLine(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                if (!line.StartsWith("[", StringComparison.Ordinal) ||
                    !line.EndsWith("]", StringComparison.Ordinal))
                {
                    continue;
                }

                string inner = ExtractBracketInner(line);

                if (inner.StartsWith("tags", StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }

            return null;
        }

        private static string ExtractBracketInner(string line)
        {
            string trimmed = line.Trim();

            if (trimmed.Length < 2)
            {
                return string.Empty;
            }

            if (trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
            {
                return string.Empty;
            }

            return trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        private static string NormalizeHeader(string inner)
        {
            string value = inner.Trim();

            value = value.Replace("Tags:", string.Empty, StringComparison.OrdinalIgnoreCase);
            value = value.Replace("Tags -", string.Empty, StringComparison.OrdinalIgnoreCase);
            value = value.Replace("Tags–", string.Empty, StringComparison.OrdinalIgnoreCase);
            value = value.Replace("Tags", string.Empty, StringComparison.OrdinalIgnoreCase);

            return value.Trim();
        }
    }
}