using System;
using System.Collections.Generic;
using System.Linq;

namespace Changsta.Ai.Infrastructure.Services.SoundCloud.Parsing
{
    public static class TagExtractor
    {
        private static readonly char[] TokenSeparators = new[] { ' ', '\t', ',', ';', '|', '/' };
        private static readonly char[] LeadingPunctuation = new[] { ':', '-', '–', '—', '·' };

        public static IReadOnlyList<string> Extract(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return Array.Empty<string>();
            }

            // Split using the three common newline variants without extra Replace allocations
            string[] lines = description.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

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

            // Tokenize on whitespace and common separators, normalize to lower-case and deduplicate
            var tokens = cleaned
                .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return tokens;
        }

        private static string? FindTagLine(string[] lines)
        {
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string line = raw.Trim();
                if (line.Length < 2 || line[0] != '[' || line[line.Length - 1] != ']')
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
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            string trimmed = line.Trim();

            if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[trimmed.Length - 1] != ']')
            {
                return string.Empty;
            }

            return trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        private static string NormalizeHeader(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner))
            {
                return string.Empty;
            }

            string value = inner.Trim();

            // If the header begins with "tags" remove it and any immediate punctuation/whitespace that follows.
            if (value.StartsWith("tags", StringComparison.OrdinalIgnoreCase))
            {
                // remove the "tags" prefix (4 chars) then strip leading whitespace and punctuation
                value = value.Substring(4).TrimStart();
                value = value.TrimStart(LeadingPunctuation).TrimStart();
            }
            else
            {
                // Fallback: remove common "Tags:" patterns anywhere (keeps behavior robust)
                value = value
                    .Replace("Tags:", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("Tags -", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("Tags–", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("Tags", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return value.Trim();
        }
    }
}